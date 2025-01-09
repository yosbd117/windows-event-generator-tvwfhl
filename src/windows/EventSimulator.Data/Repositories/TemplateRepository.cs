using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Polly;
using EventSimulator.Core.Models;
using EventSimulator.Data.Context;

namespace EventSimulator.Data.Repositories
{
    /// <summary>
    /// Repository class that handles database operations for event templates with enhanced caching,
    /// logging, and MITRE ATT&CK support. Implements enterprise-grade data access patterns with
    /// comprehensive error handling and performance optimization.
    /// </summary>
    public class TemplateRepository
    {
        private readonly EventSimulatorDbContext _context;
        private readonly ILogger<TemplateRepository> _logger;
        private readonly IMemoryCache _cache;
        private readonly IAsyncPolicy _retryPolicy;

        // Cache keys
        private const string ALL_TEMPLATES_CACHE_KEY = "AllTemplates";
        private const string TEMPLATE_CACHE_KEY_PREFIX = "Template_";
        private const string MITRE_CACHE_KEY_PREFIX = "MitreTechnique_";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Initializes a new instance of TemplateRepository with dependency injection support.
        /// </summary>
        public TemplateRepository(
            EventSimulatorDbContext context,
            ILogger<TemplateRepository> logger,
            IMemoryCache cache,
            IAsyncPolicy retryPolicy)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));

            _logger.LogInformation("Initialized TemplateRepository with caching and retry policies");
        }

        /// <summary>
        /// Retrieves all event templates with caching support.
        /// </summary>
        /// <returns>Collection of all event templates.</returns>
        public async Task<IEnumerable<EventTemplate>> GetAllTemplatesAsync()
        {
            try
            {
                // Check cache first
                if (_cache.TryGetValue(ALL_TEMPLATES_CACHE_KEY, out IEnumerable<EventTemplate> cachedTemplates))
                {
                    _logger.LogDebug("Retrieved templates from cache");
                    return cachedTemplates;
                }

                // Execute query with retry policy
                var templates = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.Templates
                        .AsNoTracking()
                        .OrderBy(t => t.Name)
                        .ToListAsync());

                // Cache the results
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(CACHE_DURATION)
                    .RegisterPostEvictionCallback((key, value, reason, state) =>
                    {
                        _logger.LogDebug("Templates cache evicted. Reason: {Reason}", reason);
                    });

                _cache.Set(ALL_TEMPLATES_CACHE_KEY, templates, cacheOptions);
                _logger.LogInformation("Retrieved and cached {Count} templates", templates.Count);

                return templates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all templates");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a specific template by ID with caching support.
        /// </summary>
        /// <param name="id">Template ID to retrieve.</param>
        /// <returns>Retrieved template or null if not found.</returns>
        public async Task<EventTemplate> GetTemplateByIdAsync(int id)
        {
            try
            {
                var cacheKey = $"{TEMPLATE_CACHE_KEY_PREFIX}{id}";

                // Check cache first
                if (_cache.TryGetValue(cacheKey, out EventTemplate cachedTemplate))
                {
                    _logger.LogDebug("Retrieved template {Id} from cache", id);
                    return cachedTemplate;
                }

                // Execute query with retry policy
                var template = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.Templates
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == id));

                if (template != null)
                {
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetSlidingExpiration(CACHE_DURATION)
                        .RegisterPostEvictionCallback((key, value, reason, state) =>
                        {
                            _logger.LogDebug("Template {Id} cache evicted. Reason: {Reason}", id, reason);
                        });

                    _cache.Set(cacheKey, template, cacheOptions);
                    _logger.LogInformation("Retrieved and cached template {Id}", id);
                }
                else
                {
                    _logger.LogWarning("Template {Id} not found", id);
                }

                return template;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving template {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Adds a new template with versioning support.
        /// </summary>
        /// <param name="template">Template to add.</param>
        /// <returns>Added template with generated ID.</returns>
        public async Task<EventTemplate> AddTemplateAsync(EventTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            try
            {
                if (!template.Validate())
                {
                    throw new InvalidOperationException("Template validation failed");
                }

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    template.ModifiedDate = DateTime.UtcNow;
                    template.CreatedDate = DateTime.UtcNow;

                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        _context.Templates.Add(template);
                        await _context.SaveChangesAsync();
                        return true;
                    });

                    await transaction.CommitAsync();

                    // Invalidate relevant cache entries
                    _cache.Remove(ALL_TEMPLATES_CACHE_KEY);
                    if (!string.IsNullOrEmpty(template.MitreAttackTechnique))
                    {
                        _cache.Remove($"{MITRE_CACHE_KEY_PREFIX}{template.MitreAttackTechnique}");
                    }

                    _logger.LogInformation("Added new template {Id}: {Name}", template.Id, template.Name);
                    return template;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding template: {Name}", template.Name);
                throw;
            }
        }

        /// <summary>
        /// Updates an existing template with optimistic concurrency.
        /// </summary>
        /// <param name="template">Template to update.</param>
        /// <returns>True if update successful, false if template not found.</returns>
        public async Task<bool> UpdateTemplateAsync(EventTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            try
            {
                if (!template.Validate())
                {
                    throw new InvalidOperationException("Template validation failed");
                }

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var existing = await _context.Templates.FindAsync(template.Id);
                    if (existing == null)
                    {
                        _logger.LogWarning("Template {Id} not found for update", template.Id);
                        return false;
                    }

                    // Version check for optimistic concurrency
                    if (existing.Version != template.Version)
                    {
                        throw new DbUpdateConcurrencyException("Template was modified by another user");
                    }

                    template.ModifiedDate = DateTime.UtcNow;
                    _context.Entry(existing).CurrentValues.SetValues(template);

                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        await _context.SaveChangesAsync();
                        return true;
                    });

                    await transaction.CommitAsync();

                    // Invalidate cache entries
                    _cache.Remove(ALL_TEMPLATES_CACHE_KEY);
                    _cache.Remove($"{TEMPLATE_CACHE_KEY_PREFIX}{template.Id}");
                    if (!string.IsNullOrEmpty(template.MitreAttackTechnique))
                    {
                        _cache.Remove($"{MITRE_CACHE_KEY_PREFIX}{template.MitreAttackTechnique}");
                    }

                    _logger.LogInformation("Updated template {Id}: {Name}", template.Id, template.Name);
                    return true;
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating template {Id}: {Name}", template.Id, template.Name);
                throw;
            }
        }

        /// <summary>
        /// Retrieves templates by MITRE ATT&CK technique with caching support.
        /// </summary>
        /// <param name="techniqueId">MITRE ATT&CK technique ID.</param>
        /// <returns>Collection of matching templates.</returns>
        public async Task<IEnumerable<EventTemplate>> GetTemplatesByMitreTechniqueAsync(string techniqueId)
        {
            if (string.IsNullOrWhiteSpace(techniqueId))
                throw new ArgumentException("MITRE technique ID cannot be empty", nameof(techniqueId));

            try
            {
                var cacheKey = $"{MITRE_CACHE_KEY_PREFIX}{techniqueId}";

                // Check cache first
                if (_cache.TryGetValue(cacheKey, out IEnumerable<EventTemplate> cachedTemplates))
                {
                    _logger.LogDebug("Retrieved MITRE technique {TechniqueId} templates from cache", techniqueId);
                    return cachedTemplates;
                }

                // Execute query with retry policy
                var templates = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.Templates
                        .AsNoTracking()
                        .Where(t => t.MitreAttackTechnique == techniqueId)
                        .OrderBy(t => t.Name)
                        .ToListAsync());

                // Cache the results
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(CACHE_DURATION)
                    .RegisterPostEvictionCallback((key, value, reason, state) =>
                    {
                        _logger.LogDebug("MITRE technique {TechniqueId} cache evicted. Reason: {Reason}", 
                            techniqueId, reason);
                    });

                _cache.Set(cacheKey, templates, cacheOptions);
                _logger.LogInformation("Retrieved and cached {Count} templates for MITRE technique {TechniqueId}", 
                    templates.Count, techniqueId);

                return templates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving templates for MITRE technique {TechniqueId}", techniqueId);
                throw;
            }
        }
    }
}
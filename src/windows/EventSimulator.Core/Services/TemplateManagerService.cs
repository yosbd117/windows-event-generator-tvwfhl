using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using System.Text.RegularExpressions;
using System.Linq;

namespace EventSimulator.Core.Services
{
    /// <summary>
    /// Implements comprehensive template management for Windows Event Log entries with support for
    /// caching, versioning, MITRE ATT&CK validation, and thread-safe operations.
    /// </summary>
    public class TemplateManagerService : ITemplateManager
    {
        private readonly ILogger<TemplateManagerService> _logger;
        private readonly IMemoryCache _cache;
        private readonly SemaphoreSlim _lock;
        private readonly IOptions<TemplateManagerOptions> _options;
        private readonly AsyncRetryPolicy _retryPolicy;
        private const string CACHE_KEY_PREFIX = "template_";
        private const int MAX_RETRY_ATTEMPTS = 3;

        public TemplateManagerService(
            ILogger<TemplateManagerService> logger,
            IMemoryCache cache,
            IOptions<TemplateManagerOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _lock = new SemaphoreSlim(1, 1);

            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    MAX_RETRY_ATTEMPTS,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "Retry {RetryCount} of {MaxRetries} after {Delay}ms",
                            retryCount,
                            MAX_RETRY_ATTEMPTS,
                            timeSpan.TotalMilliseconds);
                    }
                );
        }

        /// <inheritdoc/>
        public async Task<Result<EventTemplate>> GetTemplateAsync(
            int templateId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Retrieving template with ID: {TemplateId}", templateId);

                var cacheKey = $"{CACHE_KEY_PREFIX}{templateId}";
                if (_cache.TryGetValue(cacheKey, out EventTemplate cachedTemplate))
                {
                    _logger.LogDebug("Template {TemplateId} retrieved from cache", templateId);
                    return Result.Success(cachedTemplate);
                }

                return await HandleConcurrentOperationAsync(async () =>
                {
                    var template = await _templateRepository.GetByIdAsync(templateId, cancellationToken);
                    if (template == null)
                    {
                        return Result.Failure<EventTemplate>($"Template with ID {templateId} not found");
                    }

                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetSlidingExpiration(TimeSpan.FromMinutes(_options.Value.CacheExpirationMinutes))
                        .SetSize(1);

                    _cache.Set(cacheKey, template, cacheOptions);
                    return Result.Success(template);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving template {TemplateId}", templateId);
                return Result.Failure<EventTemplate>($"Failed to retrieve template: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<ValidationResult>> ValidateMitreAttackMappingAsync(
            EventTemplate template,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Validating MITRE ATT&CK mapping for template: {TemplateId}", template.Id);

                var validationResult = new ValidationResult { IsValid = true };

                if (string.IsNullOrWhiteSpace(template.MitreAttackTechnique))
                {
                    return Result.Success(validationResult);
                }

                // Validate MITRE ATT&CK technique ID format
                if (!Regex.IsMatch(template.MitreAttackTechnique, @"^T\d{4}(\.\d{3})?$"))
                {
                    validationResult.IsValid = false;
                    validationResult.Errors.Add($"Invalid MITRE ATT&CK technique ID format: {template.MitreAttackTechnique}");
                    return Result.Success(validationResult);
                }

                // Verify technique exists in MITRE database
                var techniqueExists = await _mitreService.ValidateTechniqueIdAsync(
                    template.MitreAttackTechnique,
                    cancellationToken);

                if (!techniqueExists)
                {
                    validationResult.IsValid = false;
                    validationResult.Errors.Add($"MITRE ATT&CK technique {template.MitreAttackTechnique} not found");
                }

                return Result.Success(validationResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating MITRE ATT&CK mapping for template {TemplateId}", template.Id);
                return Result.Failure<ValidationResult>($"MITRE ATT&CK validation failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<EventTemplate>> CreateTemplateAsync(
            EventTemplate template,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Creating new template: {TemplateName}", template.Name);

                if (!template.Validate())
                {
                    return Result.Failure<EventTemplate>("Template validation failed");
                }

                var mitreValidation = await ValidateMitreAttackMappingAsync(template, cancellationToken);
                if (!mitreValidation.IsSuccess || !mitreValidation.Value.IsValid)
                {
                    return Result.Failure<EventTemplate>("MITRE ATT&CK validation failed");
                }

                return await HandleConcurrentOperationAsync(async () =>
                {
                    var createdTemplate = await _templateRepository.CreateAsync(template, cancellationToken);
                    var cacheKey = $"{CACHE_KEY_PREFIX}{createdTemplate.Id}";
                    
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetSlidingExpiration(TimeSpan.FromMinutes(_options.Value.CacheExpirationMinutes))
                        .SetSize(1);

                    _cache.Set(cacheKey, createdTemplate, cacheOptions);
                    return Result.Success(createdTemplate);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating template: {TemplateName}", template.Name);
                return Result.Failure<EventTemplate>($"Failed to create template: {ex.Message}");
            }
        }

        private async Task<T> HandleConcurrentOperationAsync<T>(Func<Task<T>> operation)
        {
            await _lock.WaitAsync();
            try
            {
                return await _retryPolicy.ExecuteAsync(operation);
            }
            finally
            {
                _lock.Release();
            }
        }

        public class TemplateManagerOptions
        {
            public int CacheExpirationMinutes { get; set; } = 30;
            public int MaxConcurrentOperations { get; set; } = 10;
            public bool EnableMitreValidation { get; set; } = true;
        }
    }
}
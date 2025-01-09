// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.Extensions.Logging v6.0.0
// Microsoft.Extensions.Caching.Memory v6.0.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using EventSimulator.Core.Models;
using EventSimulator.Data.Context;

namespace EventSimulator.Data.Repositories
{
    /// <summary>
    /// Repository class that handles data access operations for simulation scenarios,
    /// providing CRUD operations, timeline management, scheduling capabilities,
    /// and specialized queries with caching support.
    /// </summary>
    public class ScenarioRepository
    {
        private readonly EventSimulatorDbContext _context;
        private readonly ILogger<ScenarioRepository> _logger;
        private readonly IMemoryCache _cache;
        private const string CACHE_KEY_PREFIX = "Scenario_";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Initializes a new instance of ScenarioRepository with required dependencies.
        /// </summary>
        /// <param name="context">Database context for scenario operations</param>
        /// <param name="logger">Logger instance for tracking operations</param>
        /// <param name="cache">Memory cache for optimizing frequent queries</param>
        public ScenarioRepository(
            EventSimulatorDbContext context,
            ILogger<ScenarioRepository> logger,
            IMemoryCache cache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Retrieves active scenarios filtered by category with caching support.
        /// </summary>
        /// <param name="category">Category to filter scenarios</param>
        /// <returns>Collection of active scenarios in the specified category</returns>
        public async Task<IEnumerable<ScenarioDefinition>> GetActiveScenariosByCategoryAsync(string category)
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}Category_{category}";

            if (!_cache.TryGetValue(cacheKey, out IEnumerable<ScenarioDefinition> scenarios))
            {
                _logger.LogInformation("Cache miss for category {Category}, querying database", category);

                scenarios = await _context.Scenarios
                    .Include(s => s.Events)
                    .Where(s => s.IsActive && s.Category == category)
                    .OrderBy(s => s.Name)
                    .AsNoTracking()
                    .ToListAsync();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(CACHE_DURATION)
                    .SetPriority(CacheItemPriority.Normal);

                _cache.Set(cacheKey, scenarios, cacheOptions);
            }

            return scenarios;
        }

        /// <summary>
        /// Retrieves scenarios associated with specific MITRE ATT&CK techniques.
        /// </summary>
        /// <param name="techniqueId">MITRE ATT&CK technique identifier</param>
        /// <returns>Collection of scenarios matching the technique</returns>
        public async Task<IEnumerable<ScenarioDefinition>> GetScenariosByMitreAttackTechniqueAsync(string techniqueId)
        {
            if (string.IsNullOrWhiteSpace(techniqueId))
                throw new ArgumentException("MITRE ATT&CK technique ID is required", nameof(techniqueId));

            var cacheKey = $"{CACHE_KEY_PREFIX}MITRE_{techniqueId}";

            if (!_cache.TryGetValue(cacheKey, out IEnumerable<ScenarioDefinition> scenarios))
            {
                _logger.LogInformation("Querying scenarios for MITRE technique {TechniqueId}", techniqueId);

                scenarios = await _context.Scenarios
                    .Include(s => s.Events)
                    .Where(s => s.MitreAttackReference == techniqueId && s.IsActive)
                    .OrderBy(s => s.Name)
                    .AsNoTracking()
                    .ToListAsync();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(CACHE_DURATION)
                    .SetPriority(CacheItemPriority.Normal);

                _cache.Set(cacheKey, scenarios, cacheOptions);
            }

            return scenarios;
        }

        /// <summary>
        /// Retrieves scenarios scheduled for execution within a specified time window.
        /// </summary>
        /// <param name="startTime">Start of the time window</param>
        /// <param name="endTime">End of the time window</param>
        /// <returns>Collection of scheduled scenarios within the time window</returns>
        public async Task<IEnumerable<ScenarioDefinition>> GetScheduledScenariosAsync(DateTime startTime, DateTime endTime)
        {
            if (startTime >= endTime)
                throw new ArgumentException("Start time must be before end time");

            _logger.LogInformation("Retrieving scheduled scenarios between {StartTime} and {EndTime}", 
                startTime, endTime);

            return await _context.Scenarios
                .Include(s => s.Events)
                .Where(s => s.IsActive)
                .AsNoTracking()
                .ToListAsync();
        }

        /// <summary>
        /// Validates the timeline and dependencies of a scenario.
        /// </summary>
        /// <param name="scenario">Scenario to validate</param>
        /// <returns>Validation result with any timeline issues</returns>
        public async Task<ValidationResult> ValidateScenarioTimelineAsync(ScenarioDefinition scenario)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            try
            {
                _logger.LogInformation("Validating timeline for scenario {ScenarioId}", scenario.ScenarioId);

                // Validate basic scenario properties
                if (!scenario.Validate())
                {
                    return new ValidationResult("Scenario validation failed");
                }

                // Check for cyclic dependencies
                if (!await EnsureNoCyclicDependenciesAsync(scenario))
                {
                    return new ValidationResult("Cyclic dependencies detected in scenario timeline");
                }

                // Validate event sequence
                var events = scenario.Events.OrderBy(e => e.Sequence).ToList();
                for (int i = 0; i < events.Count - 1; i++)
                {
                    if (events[i].Sequence >= events[i + 1].Sequence)
                    {
                        return new ValidationResult($"Invalid event sequence between events {events[i].ScenarioEventId} and {events[i + 1].ScenarioEventId}");
                    }
                }

                return ValidationResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating scenario timeline");
                throw;
            }
        }

        /// <summary>
        /// Checks for and prevents circular dependencies in scenario event chains.
        /// </summary>
        /// <param name="scenario">Scenario to check for cyclic dependencies</param>
        /// <returns>True if no cyclic dependencies found</returns>
        public async Task<bool> EnsureNoCyclicDependenciesAsync(ScenarioDefinition scenario)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            try
            {
                var visited = new HashSet<int>();
                var recursionStack = new HashSet<int>();

                foreach (var evt in scenario.Events)
                {
                    if (HasCyclicDependency(evt.ScenarioEventId, scenario.Events.ToList(), visited, recursionStack))
                    {
                        _logger.LogWarning("Cyclic dependency detected in scenario {ScenarioId}", scenario.ScenarioId);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for cyclic dependencies");
                throw;
            }
        }

        private bool HasCyclicDependency(int eventId, List<ScenarioEvent> events, 
            HashSet<int> visited, HashSet<int> recursionStack)
        {
            if (recursionStack.Contains(eventId))
                return true;

            if (visited.Contains(eventId))
                return false;

            visited.Add(eventId);
            recursionStack.Add(eventId);

            var currentEvent = events.FirstOrDefault(e => e.ScenarioEventId == eventId);
            if (currentEvent != null)
            {
                foreach (var dependencyId in currentEvent.DependsOnEvents)
                {
                    if (HasCyclicDependency(dependencyId, events, visited, recursionStack))
                        return true;
                }
            }

            recursionStack.Remove(eventId);
            return false;
        }
    }
}
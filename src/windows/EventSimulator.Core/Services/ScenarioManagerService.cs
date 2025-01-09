using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Services
{
    /// <summary>
    /// Provides comprehensive management of event simulation scenarios with enhanced support for
    /// MITRE ATT&CK techniques and conditional event triggers. Implements high-performance scenario
    /// execution with parallel processing capabilities.
    /// </summary>
    public class ScenarioManagerService : IScenarioManager, IDisposable
    {
        private const int DEFAULT_EXECUTION_TIMEOUT = 3600; // 1 hour default timeout
        private const int CACHE_DURATION = 300; // 5 minutes cache duration
        private const int MAX_PARALLEL_EXECUTIONS = 5;

        private readonly IEventGenerator _eventGenerator;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ScenarioManagerService> _logger;
        private readonly SemaphoreSlim _executionLock;
        private readonly ConcurrentDictionary<int, ScenarioExecutionState> _activeExecutions;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the ScenarioManagerService with required dependencies.
        /// </summary>
        public ScenarioManagerService(
            IEventGenerator eventGenerator,
            IMemoryCache cache,
            ILogger<ScenarioManagerService> logger)
        {
            _eventGenerator = eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _executionLock = new SemaphoreSlim(MAX_PARALLEL_EXECUTIONS, MAX_PARALLEL_EXECUTIONS);
            _activeExecutions = new ConcurrentDictionary<int, ScenarioExecutionState>();
            _disposed = false;
        }

        /// <inheritdoc/>
        public async Task<ScenarioDefinition> CreateScenarioAsync(
            ScenarioDefinition scenario,
            ValidationOptions validationOptions = null)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));

            _logger.LogInformation("Creating new scenario: {Name}", scenario.Name);

            validationOptions ??= new ValidationOptions();
            var validationResult = await ValidateScenarioAsync(scenario, validationOptions);

            if (!validationResult.Success)
            {
                throw new ValidationException($"Scenario validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Initialize scenario metadata
            scenario.CreatedDate = DateTime.UtcNow;
            scenario.ModifiedDate = DateTime.UtcNow;
            scenario.Version = new Version(1, 0);
            scenario.IsActive = true;

            _logger.LogInformation("Successfully created scenario {Id}: {Name}", scenario.ScenarioId, scenario.Name);
            return scenario;
        }

        /// <inheritdoc/>
        public async Task<ScenarioDefinition> UpdateScenarioAsync(
            ScenarioDefinition scenario,
            ValidationOptions validationOptions = null)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));

            if (_activeExecutions.ContainsKey(scenario.ScenarioId))
            {
                throw new InvalidOperationException($"Cannot update scenario {scenario.ScenarioId} while it is executing");
            }

            _logger.LogInformation("Updating scenario {Id}: {Name}", scenario.ScenarioId, scenario.Name);

            validationOptions ??= new ValidationOptions();
            var validationResult = await ValidateScenarioAsync(scenario, validationOptions);

            if (!validationResult.Success)
            {
                throw new ValidationException($"Scenario validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Update metadata
            scenario.ModifiedDate = DateTime.UtcNow;
            scenario.Version = new Version(scenario.Version.Major, scenario.Version.Minor + 1);

            _cache.Remove($"scenario_{scenario.ScenarioId}");
            _logger.LogInformation("Successfully updated scenario {Id}", scenario.ScenarioId);

            return scenario;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteScenarioAsync(int scenarioId, bool forceTerminate = false)
        {
            _logger.LogInformation("Deleting scenario {Id}, Force: {Force}", scenarioId, forceTerminate);

            if (_activeExecutions.TryGetValue(scenarioId, out var executionState))
            {
                if (!forceTerminate)
                {
                    throw new InvalidOperationException($"Cannot delete scenario {scenarioId} while it is executing");
                }

                executionState.CancellationTokenSource.Cancel();
                await WaitForExecutionCompletion(scenarioId);
            }

            _cache.Remove($"scenario_{scenarioId}");
            _logger.LogInformation("Successfully deleted scenario {Id}", scenarioId);

            return true;
        }

        /// <inheritdoc/>
        public async Task<ScenarioDefinition> GetScenarioAsync(int scenarioId, int? version = null)
        {
            var cacheKey = $"scenario_{scenarioId}_{version ?? 0}";

            if (_cache.TryGetValue(cacheKey, out ScenarioDefinition cachedScenario))
            {
                return cachedScenario;
            }

            // In a real implementation, this would fetch from a repository
            throw new NotImplementedException("Repository access not implemented");
        }

        /// <inheritdoc/>
        public async Task<ExecutionResult> ExecuteScenarioAsync(
            int scenarioId,
            ExecutionOptions options,
            IProgress<ScenarioProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var scenario = await GetScenarioAsync(scenarioId);
            if (scenario == null)
            {
                throw new ArgumentException($"Scenario {scenarioId} not found", nameof(scenarioId));
            }

            if (!await _executionLock.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new InvalidOperationException("Maximum concurrent scenario executions reached");
            }

            try
            {
                var executionState = new ScenarioExecutionState
                {
                    StartTime = DateTime.UtcNow,
                    CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                };

                if (options?.ExecutionTimeout != null)
                {
                    executionState.CancellationTokenSource.CancelAfter(options.ExecutionTimeout.Value);
                }
                else
                {
                    executionState.CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(DEFAULT_EXECUTION_TIMEOUT));
                }

                if (!_activeExecutions.TryAdd(scenarioId, executionState))
                {
                    throw new InvalidOperationException($"Scenario {scenarioId} is already executing");
                }

                return await ExecuteScenarioInternalAsync(scenario, options, progress, executionState.CancellationTokenSource.Token);
            }
            finally
            {
                _executionLock.Release();
                if (_activeExecutions.TryRemove(scenarioId, out var state))
                {
                    state.CancellationTokenSource.Dispose();
                }
            }
        }

        private async Task<ExecutionResult> ExecuteScenarioInternalAsync(
            ScenarioDefinition scenario,
            ExecutionOptions options,
            IProgress<ScenarioProgress> progress,
            CancellationToken cancellationToken)
        {
            var result = new ExecutionResult
            {
                Success = true,
                EventsGenerated = 0,
                EventsFailed = 0
            };

            var startTime = DateTime.UtcNow;
            var eventGroups = GroupEventsByDependencies(scenario.Events);
            var completedEvents = new HashSet<int>();
            var currentProgress = new ScenarioProgress
            {
                TotalEvents = scenario.Events.Count
            };

            try
            {
                foreach (var group in eventGroups)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    var groupTasks = group.Select(async evt =>
                    {
                        if (!await ValidateEventDependencies(evt, completedEvents))
                        {
                            result.EventsFailed++;
                            return;
                        }

                        if (evt.DelayMilliseconds > 0)
                        {
                            await Task.Delay((int)(evt.DelayMilliseconds * (options?.DelayMultiplier ?? 1.0)), cancellationToken);
                        }

                        var generationResult = await _eventGenerator.GenerateEventAsync(
                            new EventInstance(evt.Template, _logger),
                            cancellationToken);

                        if (generationResult.Success)
                        {
                            result.EventsGenerated++;
                            completedEvents.Add(evt.ScenarioEventId);
                        }
                        else
                        {
                            result.EventsFailed++;
                            if (!options?.ContinueOnError ?? false)
                            {
                                throw new InvalidOperationException($"Event generation failed: {evt.ScenarioEventId}");
                            }
                        }

                        currentProgress.EventsCompleted = result.EventsGenerated;
                        progress?.Report(currentProgress);
                    });

                    await Task.WhenAll(groupTasks);
                }

                result.ExecutionDuration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Scenario {Id} execution completed: {Success}/{Total} events in {Duration}",
                    scenario.ScenarioId,
                    result.EventsGenerated,
                    scenario.Events.Count,
                    result.ExecutionDuration);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Scenario execution was cancelled";
                _logger.LogWarning("Scenario {Id} execution cancelled", scenario.ScenarioId);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Scenario {Id} execution failed", scenario.ScenarioId);
            }

            return result;
        }

        private IEnumerable<IEnumerable<ScenarioEvent>> GroupEventsByDependencies(IEnumerable<ScenarioEvent> events)
        {
            var eventDict = events.ToDictionary(e => e.ScenarioEventId);
            var visited = new HashSet<int>();
            var groups = new List<List<ScenarioEvent>>();

            foreach (var evt in events.OrderBy(e => e.Sequence))
            {
                if (!visited.Contains(evt.ScenarioEventId))
                {
                    var group = new List<ScenarioEvent>();
                    CollectIndependentEvents(evt, eventDict, visited, group);
                    groups.Add(group);
                }
            }

            return groups;
        }

        private void CollectIndependentEvents(
            ScenarioEvent evt,
            Dictionary<int, ScenarioEvent> eventDict,
            HashSet<int> visited,
            List<ScenarioEvent> group)
        {
            if (visited.Contains(evt.ScenarioEventId)) return;

            visited.Add(evt.ScenarioEventId);
            if (!evt.DependsOnEvents.Any())
            {
                group.Add(evt);
            }

            foreach (var dependentId in eventDict.Values
                .Where(e => e.DependsOnEvents.Contains(evt.ScenarioEventId)))
            {
                CollectIndependentEvents(dependentId, eventDict, visited, group);
            }
        }

        private async Task<bool> ValidateEventDependencies(ScenarioEvent evt, HashSet<int> completedEvents)
        {
            foreach (var dependencyId in evt.DependsOnEvents)
            {
                if (!completedEvents.Contains(dependencyId))
                {
                    _logger.LogWarning(
                        "Event {EventId} dependency {DependencyId} not satisfied",
                        evt.ScenarioEventId,
                        dependencyId);
                    return false;
                }
            }
            return true;
        }

        private async Task<ValidationResult> ValidateScenarioAsync(
            ScenarioDefinition scenario,
            ValidationOptions options)
        {
            var result = new ValidationResult { Success = true, Errors = new List<string>() };

            if (options.ValidateMitreReferences && !string.IsNullOrEmpty(scenario.MitreAttackReference))
            {
                if (!await ValidateMitreReference(scenario.MitreAttackReference))
                {
                    result.Success = false;
                    result.Errors.Add($"Invalid MITRE ATT&CK reference: {scenario.MitreAttackReference}");
                }
            }

            if (options.ValidateDependencies)
            {
                var dependencyValidation = ValidateEventDependencyGraph(scenario.Events);
                if (!dependencyValidation.Success)
                {
                    result.Success = false;
                    result.Errors.AddRange(dependencyValidation.Errors);
                }
            }

            return result;
        }

        private async Task<bool> ValidateMitreReference(string reference)
        {
            // In a real implementation, this would validate against the MITRE ATT&CK database
            return System.Text.RegularExpressions.Regex.IsMatch(reference, @"^T\d{4}(\.\d{3})?$");
        }

        private ValidationResult ValidateEventDependencyGraph(IEnumerable<ScenarioEvent> events)
        {
            var result = new ValidationResult { Success = true, Errors = new List<string>() };
            var visited = new HashSet<int>();
            var recursionStack = new HashSet<int>();

            foreach (var evt in events)
            {
                if (HasCyclicDependency(evt, events.ToDictionary(e => e.ScenarioEventId), visited, recursionStack))
                {
                    result.Success = false;
                    result.Errors.Add($"Cyclic dependency detected in event {evt.ScenarioEventId}");
                }
            }

            return result;
        }

        private bool HasCyclicDependency(
            ScenarioEvent evt,
            Dictionary<int, ScenarioEvent> eventDict,
            HashSet<int> visited,
            HashSet<int> recursionStack)
        {
            if (recursionStack.Contains(evt.ScenarioEventId))
                return true;

            if (visited.Contains(evt.ScenarioEventId))
                return false;

            visited.Add(evt.ScenarioEventId);
            recursionStack.Add(evt.ScenarioEventId);

            foreach (var dependencyId in evt.DependsOnEvents)
            {
                if (eventDict.TryGetValue(dependencyId, out var dependency))
                {
                    if (HasCyclicDependency(dependency, eventDict, visited, recursionStack))
                        return true;
                }
            }

            recursionStack.Remove(evt.ScenarioEventId);
            return false;
        }

        private async Task WaitForExecutionCompletion(int scenarioId)
        {
            if (_activeExecutions.TryGetValue(scenarioId, out var state))
            {
                var timeout = TimeSpan.FromSeconds(30);
                var completionTime = DateTime.UtcNow.Add(timeout);

                while (DateTime.UtcNow < completionTime && _activeExecutions.ContainsKey(scenarioId))
                {
                    await Task.Delay(100);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var execution in _activeExecutions.Values)
            {
                execution.CancellationTokenSource.Cancel();
                execution.CancellationTokenSource.Dispose();
            }

            _executionLock.Dispose();
            _disposed = true;
        }

        private class ScenarioExecutionState
        {
            public DateTime StartTime { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
        }

        private class ValidationResult
        {
            public bool Success { get; set; }
            public List<string> Errors { get; set; }
        }
    }
}
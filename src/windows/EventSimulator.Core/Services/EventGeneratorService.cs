using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.Core.Utils;

namespace EventSimulator.Core.Services
{
    /// <summary>
    /// High-performance service for generating Windows Event Log entries with comprehensive validation,
    /// monitoring, and error handling. Implements the core event generation engine with 99.9% accuracy
    /// and 1000+ events per second performance target.
    /// </summary>
    public sealed class EventGeneratorService : IEventGenerator, IDisposable
    {
        private const int BATCH_SIZE = 1000;
        private const int MAX_PARALLEL_OPERATIONS = 4;
        private const int EVENT_HANDLE_POOL_SIZE = 16;

        private readonly IEventValidator _eventValidator;
        private readonly WindowsEventLogApi _eventLogApi;
        private readonly ILogger<EventGeneratorService> _logger;
        private readonly SemaphoreSlim _throttleSemaphore;
        private readonly ConcurrentDictionary<int, EventHandleInfo> _handlePool;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the EventGeneratorService with required dependencies.
        /// </summary>
        public EventGeneratorService(
            IEventValidator eventValidator,
            WindowsEventLogApi eventLogApi,
            ILogger<EventGeneratorService> logger)
        {
            _eventValidator = eventValidator ?? throw new ArgumentNullException(nameof(eventValidator));
            _eventLogApi = eventLogApi ?? throw new ArgumentNullException(nameof(eventLogApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _throttleSemaphore = new SemaphoreSlim(MAX_PARALLEL_OPERATIONS);
            _handlePool = new ConcurrentDictionary<int, EventHandleInfo>();
            _disposed = false;

            _logger.LogInformation("EventGeneratorService initialized with {Parallelism} parallel operations", MAX_PARALLEL_OPERATIONS);
        }

        /// <inheritdoc/>
        public async Task<EventGenerationResult> GenerateEventAsync(
            EventInstance eventInstance,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventGeneratorService));
            if (eventInstance == null) throw new ArgumentNullException(nameof(eventInstance));

            var result = new EventGenerationResult
            {
                GeneratedEvent = eventInstance,
                Messages = new List<string>()
            };

            try
            {
                var startTime = DateTime.UtcNow;

                // Validate event instance
                if (!await _eventValidator.ValidateEventInstance(eventInstance))
                {
                    result.Success = false;
                    result.Messages.Add("Event instance validation failed");
                    return result;
                }

                // Generate event XML
                eventInstance.GeneratedXml = EventXmlGenerator.GenerateEventXml(eventInstance);
                if (!EventXmlGenerator.ValidateEventXml(eventInstance.GeneratedXml))
                {
                    result.Success = false;
                    result.Messages.Add("Generated XML validation failed");
                    return result;
                }

                // Write event to Windows Event Log
                await _throttleSemaphore.WaitAsync(cancellationToken);
                try
                {
                    result.Success = await _eventLogApi.WriteEvent(eventInstance);
                }
                finally
                {
                    _throttleSemaphore.Release();
                }

                result.GenerationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("Event {EventId} generated in {TimeMs}ms", eventInstance.EventId, result.GenerationTimeMs);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Messages.Add($"Event generation failed: {ex.Message}");
                _logger.LogError(ex, "Failed to generate event {EventId}", eventInstance.EventId);
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<BatchGenerationResult> GenerateEventsAsync(
            IEnumerable<EventInstance> eventInstances,
            BatchOptions batchOptions,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventGeneratorService));
            if (eventInstances == null) throw new ArgumentNullException(nameof(eventInstances));
            if (batchOptions == null) throw new ArgumentNullException(nameof(batchOptions));

            var result = new BatchGenerationResult
            {
                EventResults = new List<EventGenerationResult>(),
                Success = true
            };

            var startTime = DateTime.UtcNow;
            var eventList = new List<EventInstance>(eventInstances);
            var tasks = new List<Task<EventGenerationResult>>();

            try
            {
                // Process events in batches
                for (int i = 0; i < eventList.Count; i += BATCH_SIZE)
                {
                    var batch = eventList.GetRange(i, Math.Min(BATCH_SIZE, eventList.Count - i));
                    var batchTasks = new List<Task<EventGenerationResult>>();

                    foreach (var evt in batch)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        batchTasks.Add(GenerateEventAsync(evt, cancellationToken));
                    }

                    var batchResults = await Task.WhenAll(batchTasks);
                    result.EventResults.AddRange(batchResults);

                    if (batchOptions.BatchDelayMs > 0)
                    {
                        await Task.Delay(batchOptions.BatchDelayMs, cancellationToken);
                    }
                }

                // Calculate batch statistics
                result.SuccessCount = result.EventResults.Count(r => r.Success);
                result.FailureCount = result.EventResults.Count - result.SuccessCount;
                result.TotalTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                result.EventsPerSecond = result.EventResults.Count / (result.TotalTimeMs / 1000.0);
                result.Success = result.FailureCount == 0;

                _logger.LogInformation(
                    "Batch generation completed: {Success}/{Total} events in {TimeMs}ms ({EventsPerSecond:F1} events/sec)",
                    result.SuccessCount,
                    result.EventResults.Count,
                    result.TotalTimeMs,
                    result.EventsPerSecond);
            }
            catch (Exception ex)
            {
                result.Success = false;
                _logger.LogError(ex, "Batch generation failed");
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<TemplateGenerationResult> GenerateFromTemplateAsync(
            EventTemplate template,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventGeneratorService));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var result = new TemplateGenerationResult
            {
                Messages = new List<string>(),
                AppliedParameters = new Dictionary<string, object>()
            };

            try
            {
                var startTime = DateTime.UtcNow;

                // Validate template
                if (!await _eventValidator.ValidateEventTemplate(template))
                {
                    result.Success = false;
                    result.Messages.Add("Template validation failed");
                    return result;
                }

                // Create event instance from template
                var eventInstance = new EventInstance(template, _logger);

                // Apply parameters
                foreach (var param in template.Parameters)
                {
                    if (parameters.TryGetValue(param.Name, out var value))
                    {
                        var eventParam = param.Clone();
                        eventParam.Value = value?.ToString();
                        eventInstance.Parameters.Add(eventParam);
                        result.AppliedParameters[param.Name] = value;
                    }
                    else if (param.IsRequired)
                    {
                        result.Success = false;
                        result.Messages.Add($"Required parameter missing: {param.Name}");
                        return result;
                    }
                }

                // Validate parameters
                if (!await _eventValidator.ValidateEventParameters(eventInstance, template))
                {
                    result.Success = false;
                    result.Messages.Add("Parameter validation failed");
                    return result;
                }

                // Generate event
                var generationResult = await GenerateEventAsync(eventInstance, cancellationToken);
                result.Success = generationResult.Success;
                result.GeneratedEvent = generationResult.GeneratedEvent;
                result.Messages.AddRange(generationResult.Messages);
                result.ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Messages.Add($"Template generation failed: {ex.Message}");
                _logger.LogError(ex, "Failed to generate event from template {TemplateId}", template.Id);
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _throttleSemaphore.Dispose();
            _disposed = true;
        }

        private class EventHandleInfo
        {
            public IntPtr Handle { get; set; }
            public DateTime LastUsed { get; set; }
        }
    }
}
// System v6.0.0 - Basic .NET functionality including collections and async support
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Internal imports for event instance and template handling
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Interfaces
{
    /// <summary>
    /// Defines the core contract for generating synthetic Windows Event Log entries with high performance
    /// and accuracy requirements. Supports single event generation, batch processing, and template-based
    /// event creation with comprehensive validation and monitoring capabilities.
    /// </summary>
    public interface IEventGenerator
    {
        /// <summary>
        /// Generates a single Windows Event Log entry asynchronously with comprehensive validation.
        /// Ensures 99.9% conformance to Windows Event Log specifications.
        /// </summary>
        /// <param name="eventInstance">The event instance containing all required event data.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A task containing the detailed event generation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when eventInstance is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when lacking required permissions.</exception>
        /// <exception cref="InvalidOperationException">Thrown when event generation fails.</exception>
        Task<EventGenerationResult> GenerateEventAsync(
            EventInstance eventInstance,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates multiple Windows Event Log entries asynchronously with optimized batch processing.
        /// Supports generation of 1000+ events per second through parallel processing.
        /// </summary>
        /// <param name="eventInstances">Collection of event instances to generate.</param>
        /// <param name="batchOptions">Configuration options for batch processing.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A task containing the comprehensive batch generation results.</returns>
        /// <exception cref="ArgumentNullException">Thrown when eventInstances or batchOptions is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when batch generation fails.</exception>
        Task<BatchGenerationResult> GenerateEventsAsync(
            IEnumerable<EventInstance> eventInstances,
            BatchOptions batchOptions,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates Windows Event Log entries from a template with parameter substitution.
        /// Provides validation of template structure and parameter values.
        /// </summary>
        /// <param name="template">The event template to use for generation.</param>
        /// <param name="parameters">Dictionary of parameter values to apply to the template.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A task containing the template-based generation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when template is null.</exception>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when template generation fails.</exception>
        Task<TemplateGenerationResult> GenerateFromTemplateAsync(
            EventTemplate template,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents the result of a single event generation operation including validation details
    /// and performance metrics.
    /// </summary>
    public class EventGenerationResult
    {
        /// <summary>
        /// Gets or sets whether the event was successfully generated.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the generated event instance with updated status.
        /// </summary>
        public EventInstance GeneratedEvent { get; set; }

        /// <summary>
        /// Gets or sets any validation messages or errors encountered.
        /// </summary>
        public IList<string> Messages { get; set; }

        /// <summary>
        /// Gets or sets the time taken to generate the event in milliseconds.
        /// </summary>
        public long GenerationTimeMs { get; set; }
    }

    /// <summary>
    /// Represents the result of a batch event generation operation including performance metrics
    /// and individual event statuses.
    /// </summary>
    public class BatchGenerationResult
    {
        /// <summary>
        /// Gets or sets the overall success status of the batch operation.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the collection of individual event generation results.
        /// </summary>
        public IList<EventGenerationResult> EventResults { get; set; }

        /// <summary>
        /// Gets or sets the total number of events successfully generated.
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of failed event generations.
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Gets or sets the total time taken for batch generation in milliseconds.
        /// </summary>
        public long TotalTimeMs { get; set; }

        /// <summary>
        /// Gets or sets the events generated per second rate.
        /// </summary>
        public double EventsPerSecond { get; set; }
    }

    /// <summary>
    /// Represents the result of a template-based event generation operation including
    /// parameter validation and substitution details.
    /// </summary>
    public class TemplateGenerationResult
    {
        /// <summary>
        /// Gets or sets whether the template-based generation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the generated event instance.
        /// </summary>
        public EventInstance GeneratedEvent { get; set; }

        /// <summary>
        /// Gets or sets any parameter validation or substitution messages.
        /// </summary>
        public IList<string> Messages { get; set; }

        /// <summary>
        /// Gets or sets the collection of parameters that were successfully applied.
        /// </summary>
        public IDictionary<string, object> AppliedParameters { get; set; }

        /// <summary>
        /// Gets or sets the time taken for template processing in milliseconds.
        /// </summary>
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// Defines configuration options for batch event generation operations.
    /// </summary>
    public class BatchOptions
    {
        /// <summary>
        /// Gets or sets the maximum degree of parallelism for batch processing.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the batch size for grouped processing.
        /// </summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the delay between batch operations in milliseconds.
        /// </summary>
        public int BatchDelayMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets whether to continue processing on individual event failures.
        /// </summary>
        public bool ContinueOnError { get; set; } = true;
    }
}
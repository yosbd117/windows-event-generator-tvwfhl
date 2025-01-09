// External package versions:
// System.Threading.Tasks v6.0.0
// System.Threading v6.0.0
// System v6.0.0

using System;
using System.Threading;
using System.Threading.Tasks;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for managing and executing event simulation scenarios that replicate
    /// complex security incidents and system behaviors. Provides comprehensive scenario lifecycle
    /// management with validation, execution control, and progress monitoring capabilities.
    /// </summary>
    public interface IScenarioManager
    {
        /// <summary>
        /// Creates a new event simulation scenario with the specified configuration and event sequence.
        /// </summary>
        /// <param name="scenario">The scenario definition containing events, timing, and dependencies.</param>
        /// <param name="validationOptions">Optional validation options to customize validation rules.</param>
        /// <returns>The newly created scenario with generated ID and metadata.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scenario is null.</exception>
        /// <exception cref="ValidationException">Thrown when scenario validation fails.</exception>
        Task<ScenarioDefinition> CreateScenarioAsync(
            ScenarioDefinition scenario,
            ValidationOptions validationOptions = null);

        /// <summary>
        /// Updates an existing scenario with modified configuration or event sequence.
        /// </summary>
        /// <param name="scenario">The updated scenario definition.</param>
        /// <param name="validationOptions">Optional validation options to customize validation rules.</param>
        /// <returns>The updated scenario definition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scenario is null.</exception>
        /// <exception cref="ValidationException">Thrown when scenario validation fails.</exception>
        /// <exception cref="InvalidOperationException">Thrown when scenario is currently executing.</exception>
        Task<ScenarioDefinition> UpdateScenarioAsync(
            ScenarioDefinition scenario,
            ValidationOptions validationOptions = null);

        /// <summary>
        /// Deletes an existing scenario by its ID.
        /// </summary>
        /// <param name="scenarioId">The unique identifier of the scenario to delete.</param>
        /// <param name="forceTerminate">If true, terminates any active executions before deletion.</param>
        /// <returns>True if deletion was successful, false if scenario not found.</returns>
        /// <exception cref="InvalidOperationException">Thrown when scenario is executing and forceTerminate is false.</exception>
        Task<bool> DeleteScenarioAsync(int scenarioId, bool forceTerminate = false);

        /// <summary>
        /// Retrieves a scenario by its ID with optional version specification.
        /// </summary>
        /// <param name="scenarioId">The unique identifier of the scenario to retrieve.</param>
        /// <param name="version">Optional specific version to retrieve. Null returns latest version.</param>
        /// <returns>The requested scenario definition if found, null otherwise.</returns>
        Task<ScenarioDefinition> GetScenarioAsync(int scenarioId, int? version = null);

        /// <summary>
        /// Executes a scenario by generating its events in sequence with specified delays and conditions.
        /// </summary>
        /// <param name="scenarioId">The unique identifier of the scenario to execute.</param>
        /// <param name="options">Execution options including timing and validation settings.</param>
        /// <param name="progress">Optional progress reporting callback.</param>
        /// <param name="cancellationToken">Optional cancellation token to stop execution.</param>
        /// <returns>Detailed execution results including success status and generated events.</returns>
        /// <exception cref="ArgumentException">Thrown when scenario ID is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when scenario is already executing.</exception>
        /// <exception cref="OperationCanceledException">Thrown when execution is cancelled.</exception>
        Task<ExecutionResult> ExecuteScenarioAsync(
            int scenarioId,
            ExecutionOptions options,
            IProgress<ScenarioProgress> progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Defines options for scenario validation during creation and updates.
    /// </summary>
    public class ValidationOptions
    {
        /// <summary>
        /// Gets or sets whether to validate event dependencies.
        /// </summary>
        public bool ValidateDependencies { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to validate MITRE ATT&CK references.
        /// </summary>
        public bool ValidateMitreReferences { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to validate event parameter constraints.
        /// </summary>
        public bool ValidateParameters { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enforce strict template validation.
        /// </summary>
        public bool StrictTemplateValidation { get; set; } = true;
    }

    /// <summary>
    /// Defines options for scenario execution.
    /// </summary>
    public class ExecutionOptions
    {
        /// <summary>
        /// Gets or sets whether to validate events before execution.
        /// </summary>
        public bool ValidateBeforeExecution { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to continue execution after individual event failures.
        /// </summary>
        public bool ContinueOnError { get; set; } = false;

        /// <summary>
        /// Gets or sets the timeout duration for the entire scenario execution.
        /// </summary>
        public TimeSpan? ExecutionTimeout { get; set; }

        /// <summary>
        /// Gets or sets the delay multiplier for event timing.
        /// </summary>
        public double DelayMultiplier { get; set; } = 1.0;
    }

    /// <summary>
    /// Represents the progress of scenario execution.
    /// </summary>
    public class ScenarioProgress
    {
        /// <summary>
        /// Gets or sets the number of events completed.
        /// </summary>
        public int EventsCompleted { get; set; }

        /// <summary>
        /// Gets or sets the total number of events in the scenario.
        /// </summary>
        public int TotalEvents { get; set; }

        /// <summary>
        /// Gets or sets the current execution phase.
        /// </summary>
        public string CurrentPhase { get; set; }

        /// <summary>
        /// Gets or sets any error message from the last event execution.
        /// </summary>
        public string LastError { get; set; }
    }

    /// <summary>
    /// Represents the results of scenario execution.
    /// </summary>
    public class ExecutionResult
    {
        /// <summary>
        /// Gets or sets whether the overall execution was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the number of events successfully generated.
        /// </summary>
        public int EventsGenerated { get; set; }

        /// <summary>
        /// Gets or sets the number of events that failed to generate.
        /// </summary>
        public int EventsFailed { get; set; }

        /// <summary>
        /// Gets or sets the total execution duration.
        /// </summary>
        public TimeSpan ExecutionDuration { get; set; }

        /// <summary>
        /// Gets or sets any error message from the execution.
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}
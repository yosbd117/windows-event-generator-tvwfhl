// External package versions:
// System v6.0.0
// System.Collections.Generic v6.0.0
// System.ComponentModel.DataAnnotations v6.0.0
// System.Text.Json v6.0.0

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace EventSimulator.Core.Models
{
    /// <summary>
    /// Represents a complete event simulation scenario with multiple events, timing, dependencies,
    /// and MITRE ATT&CK technique mapping. Provides thread-safe event management and comprehensive validation.
    /// </summary>
    [Serializable]
    public class ScenarioDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier for this scenario.
        /// </summary>
        [Required]
        public int ScenarioId { get; set; }

        /// <summary>
        /// Gets or sets the name of the scenario. Must be unique and descriptive.
        /// </summary>
        [Required(ErrorMessage = "Scenario name is required")]
        [StringLength(200, MinimumLength = 3, ErrorMessage = "Scenario name must be between 3 and 200 characters")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the detailed description of the scenario and its purpose.
        /// </summary>
        [Required(ErrorMessage = "Scenario description is required")]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the category for organizational purposes.
        /// </summary>
        [Required(ErrorMessage = "Category is required")]
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the MITRE ATT&CK technique reference for this scenario.
        /// Format must match T####.### pattern.
        /// </summary>
        [RegularExpression(@"^T\d{4}(\.\d{3})?$", ErrorMessage = "Invalid MITRE ATT&CK technique ID format")]
        public string MitreAttackReference { get; set; }

        /// <summary>
        /// Gets or sets the version of this scenario definition.
        /// </summary>
        [Required]
        public Version Version { get; set; }

        /// <summary>
        /// Gets the UTC timestamp when this scenario was created.
        /// </summary>
        public DateTime CreatedDate { get; private set; }

        /// <summary>
        /// Gets the UTC timestamp when this scenario was last modified.
        /// </summary>
        public DateTime ModifiedDate { get; private set; }

        /// <summary>
        /// Gets or sets whether this scenario is active and available for execution.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets the JSON configuration for scenario-specific settings.
        /// </summary>
        public JsonDocument Configuration { get; set; }

        /// <summary>
        /// Gets the thread-safe collection of events in this scenario.
        /// </summary>
        public ConcurrentBag<ScenarioEvent> Events { get; private set; }

        /// <summary>
        /// Gets or sets the current validation status of the scenario.
        /// </summary>
        public string ValidationStatus { get; private set; }

        /// <summary>
        /// Gets the list of validation errors if any are present.
        /// </summary>
        public List<string> ValidationErrors { get; private set; }

        /// <summary>
        /// Initializes a new instance of the ScenarioDefinition class with default values
        /// and thread-safe collections.
        /// </summary>
        public ScenarioDefinition()
        {
            Events = new ConcurrentBag<ScenarioEvent>();
            ValidationErrors = new List<string>();
            CreatedDate = DateTime.UtcNow;
            ModifiedDate = DateTime.UtcNow;
            Version = new Version(1, 0);
            IsActive = false;
            ValidationStatus = "Pending";
        }

        /// <summary>
        /// Performs comprehensive validation of the scenario configuration including MITRE ATT&CK
        /// reference and event dependencies.
        /// </summary>
        /// <returns>True if the scenario configuration is valid, false otherwise.</returns>
        public bool Validate()
        {
            ValidationErrors.Clear();

            // Validate required fields
            if (string.IsNullOrWhiteSpace(Name))
                ValidationErrors.Add("Scenario name is required");
            if (string.IsNullOrWhiteSpace(Category))
                ValidationErrors.Add("Category is required");

            // Validate MITRE ATT&CK reference if provided
            if (!string.IsNullOrWhiteSpace(MitreAttackReference))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(MitreAttackReference, @"^T\d{4}(\.\d{3})?$"))
                    ValidationErrors.Add("Invalid MITRE ATT&CK technique ID format");
            }

            // Validate events collection
            if (!Events.Any())
            {
                ValidationErrors.Add("Scenario must contain at least one event");
            }
            else
            {
                // Validate each event
                foreach (var evt in Events)
                {
                    if (!evt.Validate())
                        ValidationErrors.Add($"Invalid event configuration for event sequence {evt.Sequence}");
                }

                // Check for unique sequence numbers
                var sequences = Events.Select(e => e.Sequence).ToList();
                if (sequences.Count != sequences.Distinct().Count())
                    ValidationErrors.Add("Duplicate event sequence numbers detected");

                // Validate event dependencies
                if (!ValidateEventDependencies())
                    ValidationErrors.Add("Invalid event dependency configuration detected");
            }

            // Validate Configuration JSON if present
            if (Configuration != null)
            {
                try
                {
                    using (JsonDocument.Parse(Configuration.RootElement.GetRawText()))
                    {
                        // Additional configuration schema validation could be implemented here
                    }
                }
                catch (JsonException)
                {
                    ValidationErrors.Add("Invalid scenario configuration JSON");
                }
            }

            ValidationStatus = ValidationErrors.Count == 0 ? "Valid" : "Invalid";
            return ValidationErrors.Count == 0;
        }

        /// <summary>
        /// Creates a deep copy of the scenario with proper handling of all properties and collections.
        /// </summary>
        /// <returns>A new instance with deep-copied values.</returns>
        public ScenarioDefinition Clone()
        {
            var clone = new ScenarioDefinition
            {
                ScenarioId = this.ScenarioId,
                Name = this.Name,
                Description = this.Description,
                Category = this.Category,
                MitreAttackReference = this.MitreAttackReference,
                IsActive = this.IsActive,
                Version = new Version(this.Version.Major, this.Version.Minor + 1),
                ModifiedDate = DateTime.UtcNow
            };

            // Deep copy events
            foreach (var evt in this.Events)
            {
                clone.Events.Add(evt.Clone());
            }

            // Clone configuration if present
            if (this.Configuration != null)
            {
                clone.Configuration = JsonDocument.Parse(
                    this.Configuration.RootElement.GetRawText()
                );
            }

            // Copy validation state
            clone.ValidationErrors = new List<string>(this.ValidationErrors);
            clone.ValidationStatus = "Pending";

            return clone;
        }

        /// <summary>
        /// Validates that event dependencies form a valid directed acyclic graph.
        /// </summary>
        /// <returns>True if dependencies are valid, false if cycles are detected.</returns>
        private bool ValidateEventDependencies()
        {
            var visited = new HashSet<int>();
            var recursionStack = new HashSet<int>();

            foreach (var evt in Events)
            {
                if (HasCyclicDependency(evt.ScenarioEventId, visited, recursionStack))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively checks for cyclic dependencies in event relationships.
        /// </summary>
        private bool HasCyclicDependency(int eventId, HashSet<int> visited, HashSet<int> recursionStack)
        {
            if (recursionStack.Contains(eventId))
                return true;

            if (visited.Contains(eventId))
                return false;

            visited.Add(eventId);
            recursionStack.Add(eventId);

            var currentEvent = Events.FirstOrDefault(e => e.ScenarioEventId == eventId);
            if (currentEvent != null)
            {
                foreach (var dependencyId in currentEvent.DependsOnEvents)
                {
                    if (HasCyclicDependency(dependencyId, visited, recursionStack))
                        return true;
                }
            }

            recursionStack.Remove(eventId);
            return false;
        }
    }
}
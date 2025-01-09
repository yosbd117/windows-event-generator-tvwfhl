// External package versions:
// System v6.0.0
// System.Collections.Concurrent v6.0.0
// System.ComponentModel.DataAnnotations v6.0.0
// System.Text.Json v6.0.0

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace EventSimulator.Core.Models
{
    /// <summary>
    /// Represents an individual event within a simulation scenario, defining its template,
    /// timing, parameters, dependencies, and conditional execution rules.
    /// This class is thread-safe for concurrent scenario execution.
    /// </summary>
    public class ScenarioEvent
    {
        /// <summary>
        /// Gets or sets the unique identifier for this scenario event.
        /// </summary>
        [Required]
        public int ScenarioEventId { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the parent scenario.
        /// </summary>
        [Required]
        public int ScenarioId { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the event template to be used.
        /// </summary>
        [Required]
        public int TemplateId { get; set; }

        /// <summary>
        /// Gets or sets the sequence number determining the event's position in the scenario.
        /// </summary>
        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Sequence must be non-negative")]
        public int Sequence { get; set; }

        /// <summary>
        /// Gets or sets the delay in milliseconds before executing this event.
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "Delay must be non-negative")]
        public int DelayMilliseconds { get; set; }

        /// <summary>
        /// Gets the thread-safe collection of parameter overrides for this event.
        /// </summary>
        public ConcurrentDictionary<string, object> Parameters { get; private set; }

        /// <summary>
        /// Gets the list of event IDs that must complete before this event can execute.
        /// </summary>
        public List<int> DependsOnEvents { get; private set; }

        /// <summary>
        /// Gets or sets the JSON document defining conditional execution rules.
        /// </summary>
        public JsonDocument Conditions { get; set; }

        /// <summary>
        /// Gets or sets whether this event is enabled for execution.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Gets or sets the associated event template.
        /// </summary>
        public EventTemplate Template { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the last execution attempt.
        /// </summary>
        public DateTime? LastExecutionTime { get; set; }

        /// <summary>
        /// Gets or sets whether the last execution was successful.
        /// </summary>
        public bool ExecutionSucceeded { get; set; }

        /// <summary>
        /// Initializes a new instance of the ScenarioEvent class with default values
        /// and thread-safe collections.
        /// </summary>
        public ScenarioEvent()
        {
            Parameters = new ConcurrentDictionary<string, object>();
            DependsOnEvents = new List<int>();
            IsEnabled = true;
            DelayMilliseconds = 0;
            LastExecutionTime = null;
            ExecutionSucceeded = false;
        }

        /// <summary>
        /// Validates the event configuration including dependencies, parameters, and conditions.
        /// </summary>
        /// <returns>True if the event configuration is valid, false otherwise.</returns>
        public bool Validate()
        {
            // Validate basic requirements
            if (TemplateId <= 0 || ScenarioId <= 0)
            {
                return false;
            }

            // Validate sequence and delay
            if (Sequence < 0 || DelayMilliseconds < 0)
            {
                return false;
            }

            // Validate template if available
            if (Template != null && !Template.Validate())
            {
                return false;
            }

            // Validate parameters against template if available
            if (Template != null)
            {
                foreach (var parameter in Template.Parameters)
                {
                    if (parameter.IsRequired && 
                        !Parameters.ContainsKey(parameter.Name))
                    {
                        return false;
                    }
                }
            }

            // Validate conditions if present
            if (Conditions != null)
            {
                try
                {
                    // Verify JSON structure is valid for conditions
                    using (JsonDocument.Parse(Conditions.RootElement.GetRawText()))
                    {
                        // Additional condition syntax validation could be implemented here
                    }
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            // Validate dependencies
            if (DependsOnEvents.Contains(ScenarioEventId))
            {
                return false; // Prevent self-dependency
            }

            return true;
        }

        /// <summary>
        /// Creates a deep copy of the scenario event.
        /// </summary>
        /// <returns>A new instance of ScenarioEvent with copied values.</returns>
        public ScenarioEvent Clone()
        {
            var clone = new ScenarioEvent
            {
                ScenarioEventId = this.ScenarioEventId,
                ScenarioId = this.ScenarioId,
                TemplateId = this.TemplateId,
                Sequence = this.Sequence,
                DelayMilliseconds = this.DelayMilliseconds,
                IsEnabled = this.IsEnabled,
                LastExecutionTime = this.LastExecutionTime,
                ExecutionSucceeded = this.ExecutionSucceeded
            };

            // Deep copy parameters
            foreach (var param in this.Parameters)
            {
                clone.Parameters.TryAdd(param.Key, param.Value);
            }

            // Deep copy dependencies
            clone.DependsOnEvents.AddRange(this.DependsOnEvents);

            // Clone conditions if present
            if (this.Conditions != null)
            {
                clone.Conditions = JsonDocument.Parse(
                    this.Conditions.RootElement.GetRawText()
                );
            }

            // Clone template if present
            if (this.Template != null)
            {
                clone.Template = this.Template.Clone();
            }

            return clone;
        }

        /// <summary>
        /// Evaluates the conditions JSON document to determine if the event should execute.
        /// </summary>
        /// <param name="contextData">Dictionary containing context data for condition evaluation.</param>
        /// <returns>True if conditions are met or no conditions exist.</returns>
        public bool EvaluateConditions(IDictionary<string, object> contextData)
        {
            if (Conditions == null)
            {
                return true; // No conditions means always execute
            }

            try
            {
                var root = Conditions.RootElement;

                // Recursive function to evaluate condition nodes
                bool EvaluateNode(JsonElement node)
                {
                    if (node.TryGetProperty("operator", out JsonElement op))
                    {
                        switch (op.GetString().ToLower())
                        {
                            case "and":
                                var andConditions = node.GetProperty("conditions").EnumerateArray();
                                return andConditions.All(c => EvaluateNode(c));

                            case "or":
                                var orConditions = node.GetProperty("conditions").EnumerateArray();
                                return orConditions.Any(c => EvaluateNode(c));

                            case "equals":
                            case "notequals":
                            case "greaterthan":
                            case "lessthan":
                                var field = node.GetProperty("field").GetString();
                                var value = node.GetProperty("value");

                                if (!contextData.ContainsKey(field))
                                {
                                    return false;
                                }

                                var contextValue = contextData[field];
                                var comparison = CompareValues(contextValue, value);

                                return op.GetString().ToLower() switch
                                {
                                    "equals" => comparison == 0,
                                    "notequals" => comparison != 0,
                                    "greaterthan" => comparison > 0,
                                    "lessthan" => comparison < 0,
                                    _ => false
                                };
                        }
                    }

                    return false;
                }

                return EvaluateNode(root);
            }
            catch (Exception)
            {
                return false; // Any error in condition evaluation fails closed
            }
        }

        /// <summary>
        /// Compares two values for condition evaluation.
        /// </summary>
        private static int CompareValues(object contextValue, JsonElement conditionValue)
        {
            return conditionValue.ValueKind switch
            {
                JsonValueKind.String => string.Compare(
                    contextValue?.ToString(),
                    conditionValue.GetString(),
                    StringComparison.OrdinalIgnoreCase
                ),
                JsonValueKind.Number => Comparer<double>.Default.Compare(
                    Convert.ToDouble(contextValue),
                    conditionValue.GetDouble()
                ),
                JsonValueKind.True or JsonValueKind.False => Comparer<bool>.Default.Compare(
                    Convert.ToBoolean(contextValue),
                    conditionValue.GetBoolean()
                ),
                _ => throw new ArgumentException("Unsupported value type for comparison")
            };
        }
    }
}
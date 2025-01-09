// External package versions:
// System.ComponentModel.DataAnnotations - v6.0.0
// System.Text.Json - v6.0.0

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EventSimulator.Core.Models
{
    /// <summary>
    /// Represents a customizable parameter within a Windows Event Log entry with comprehensive validation 
    /// and templating support. Provides functionality for parameter definition, validation, and reuse
    /// across event templates and instances.
    /// </summary>
    [Serializable]
    public class EventParameter
    {
        /// <summary>
        /// Gets or sets the unique identifier for the parameter.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the parameter. This is required and must be unique within an event context.
        /// </summary>
        [Required(ErrorMessage = "Parameter name is required")]
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the value of the parameter. May be required based on IsRequired property.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; set; }

        /// <summary>
        /// Gets or sets the data type of the parameter. Defaults to "string".
        /// Supported types include: string, int, long, datetime, bool, guid
        /// </summary>
        [Required(ErrorMessage = "Parameter data type is required")]
        [JsonPropertyName("dataType")]
        public string DataType { get; set; }

        /// <summary>
        /// Gets or sets the description of the parameter for documentation purposes.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets whether the parameter is required when generating events.
        /// </summary>
        [JsonPropertyName("isRequired")]
        public bool IsRequired { get; set; }

        /// <summary>
        /// Gets or sets the regular expression pattern used for validating the parameter value.
        /// </summary>
        [JsonPropertyName("validationPattern")]
        public string ValidationPattern { get; set; }

        /// <summary>
        /// Initializes a new instance of the EventParameter class with default values.
        /// </summary>
        public EventParameter()
        {
            Id = 0;
            Name = string.Empty;
            Value = string.Empty;
            DataType = "string";
            Description = string.Empty;
            IsRequired = false;
            ValidationPattern = null;
        }

        /// <summary>
        /// Performs comprehensive validation of the parameter against its constraints and validation pattern.
        /// </summary>
        /// <returns>True if the parameter is valid, false otherwise.</returns>
        public bool Validate()
        {
            // Validate name is not null or empty
            if (string.IsNullOrWhiteSpace(Name))
            {
                return false;
            }

            // Validate required value
            if (IsRequired && string.IsNullOrWhiteSpace(Value))
            {
                return false;
            }

            // Skip further validation if value is empty and not required
            if (string.IsNullOrWhiteSpace(Value) && !IsRequired)
            {
                return true;
            }

            // Validate data type
            try
            {
                switch (DataType.ToLowerInvariant())
                {
                    case "int":
                        int.Parse(Value);
                        break;
                    case "long":
                        long.Parse(Value);
                        break;
                    case "datetime":
                        DateTime.Parse(Value);
                        break;
                    case "bool":
                        bool.Parse(Value);
                        break;
                    case "guid":
                        Guid.Parse(Value);
                        break;
                    case "string":
                        // String values are always valid
                        break;
                    default:
                        return false; // Unsupported data type
                }
            }
            catch
            {
                return false; // Data type validation failed
            }

            // Validate against pattern if specified
            if (!string.IsNullOrWhiteSpace(ValidationPattern))
            {
                try
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(Value, ValidationPattern))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false; // Invalid regex pattern
                }
            }

            return true;
        }

        /// <summary>
        /// Creates a deep copy of the parameter for template reuse.
        /// </summary>
        /// <returns>A new instance of EventParameter with copied values.</returns>
        public EventParameter Clone()
        {
            return new EventParameter
            {
                Id = this.Id,
                Name = this.Name,
                Value = this.Value,
                DataType = this.DataType,
                Description = this.Description,
                IsRequired = this.IsRequired,
                ValidationPattern = this.ValidationPattern
            };
        }
    }
}
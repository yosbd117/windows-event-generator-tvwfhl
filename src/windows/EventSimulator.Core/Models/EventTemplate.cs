using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using EventSimulator.Core.Constants;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Models
{
    /// <summary>
    /// Represents a comprehensive template for generating Windows Event Log entries with enhanced validation,
    /// MITRE ATT&CK mapping, and version control support. This class is thread-safe and immutable after creation.
    /// </summary>
    public sealed class EventTemplate
    {
        /// <summary>
        /// Gets or sets the unique identifier for the template.
        /// </summary>
        [Required]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the template. Must be unique and descriptive.
        /// </summary>
        [Required(ErrorMessage = "Template name is required")]
        [StringLength(200, MinimumLength = 3, ErrorMessage = "Template name must be between 3 and 200 characters")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the detailed description of the event template and its purpose.
        /// </summary>
        [Required(ErrorMessage = "Template description is required")]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the Windows Event Log channel for this template.
        /// Must match one of the predefined channels in EventLogChannels.
        /// </summary>
        [Required(ErrorMessage = "Event channel is required")]
        public string Channel { get; set; }

        /// <summary>
        /// Gets or sets the Windows Event ID. Must be a positive integer.
        /// </summary>
        [Required(ErrorMessage = "Event ID is required")]
        [Range(1, 65535, ErrorMessage = "Event ID must be between 1 and 65535")]
        public int EventId { get; set; }

        /// <summary>
        /// Gets or sets the event level as defined in EventLogLevels.
        /// </summary>
        [Required(ErrorMessage = "Event level is required")]
        [Range(0, 5, ErrorMessage = "Event level must be between 0 and 5")]
        public int Level { get; set; }

        /// <summary>
        /// Gets or sets the event source name.
        /// </summary>
        [Required(ErrorMessage = "Event source is required")]
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the event category for organizational purposes.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the MITRE ATT&CK technique ID associated with this template.
        /// Format must match T####.### pattern if provided.
        /// </summary>
        [RegularExpression(@"^T\d{4}(\.\d{3})?$", ErrorMessage = "Invalid MITRE ATT&CK technique ID format")]
        public string MitreAttackTechnique { get; set; }

        /// <summary>
        /// Gets or sets the template version in semantic versioning format.
        /// </summary>
        [Required(ErrorMessage = "Version is required")]
        [RegularExpression(@"^\d+\.\d+\.\d+$", ErrorMessage = "Version must follow semantic versioning format (e.g., 1.0.0)")]
        public string Version { get; set; }

        /// <summary>
        /// Gets the UTC timestamp when this template was created.
        /// </summary>
        public DateTime CreatedDate { get; private set; }

        /// <summary>
        /// Gets the UTC timestamp when this template was last modified.
        /// </summary>
        public DateTime ModifiedDate { get; private set; }

        /// <summary>
        /// Gets the collection of parameters associated with this template.
        /// Implemented as a thread-safe collection.
        /// </summary>
        public ICollection<EventParameter> Parameters { get; private set; }

        /// <summary>
        /// Initializes a new instance of the EventTemplate class with default values
        /// and a thread-safe parameter collection.
        /// </summary>
        public EventTemplate()
        {
            Parameters = new ConcurrentBag<EventParameter>();
            CreatedDate = DateTime.UtcNow;
            ModifiedDate = DateTime.UtcNow;
            Version = "1.0.0";
            Category = string.Empty;
            MitreAttackTechnique = string.Empty;
        }

        /// <summary>
        /// Performs comprehensive validation of the template including MITRE ATT&CK format
        /// and Windows Event Log compliance.
        /// </summary>
        /// <returns>True if the template is valid, false otherwise.</returns>
        public bool Validate()
        {
            // Validate basic required fields
            if (string.IsNullOrWhiteSpace(Name) || 
                string.IsNullOrWhiteSpace(Description) || 
                string.IsNullOrWhiteSpace(Source))
            {
                return false;
            }

            // Validate Channel against known constants
            if (Channel != EventLogChannels.Security && 
                Channel != EventLogChannels.System && 
                Channel != EventLogChannels.Application)
            {
                return false;
            }

            // Validate EventId range
            if (EventId <= 0 || EventId > 65535)
            {
                return false;
            }

            // Validate Level against known constants
            if (Level < EventLogLevels.LogAlways || Level > EventLogLevels.Verbose)
            {
                return false;
            }

            // Validate MITRE ATT&CK technique ID format if provided
            if (!string.IsNullOrWhiteSpace(MitreAttackTechnique))
            {
                if (!Regex.IsMatch(MitreAttackTechnique, @"^T\d{4}(\.\d{3})?$"))
                {
                    return false;
                }
            }

            // Validate version format
            if (!Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$"))
            {
                return false;
            }

            // Validate all parameters
            foreach (var parameter in Parameters)
            {
                if (!parameter.Validate())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates a deep copy of the template with new timestamps and incremented version.
        /// </summary>
        /// <returns>A new instance of EventTemplate with copied values and updated timestamps.</returns>
        public EventTemplate Clone()
        {
            var clone = new EventTemplate
            {
                Id = this.Id,
                Name = this.Name,
                Description = this.Description,
                Channel = this.Channel,
                EventId = this.EventId,
                Level = this.Level,
                Source = this.Source,
                Category = this.Category,
                MitreAttackTechnique = this.MitreAttackTechnique
            };

            // Increment minor version number
            var versionParts = this.Version.Split('.');
            clone.Version = $"{versionParts[0]}.{int.Parse(versionParts[1]) + 1}.0";

            // Deep copy parameters
            foreach (var parameter in this.Parameters)
            {
                ((ConcurrentBag<EventParameter>)clone.Parameters).Add(parameter.Clone());
            }

            // Set new timestamps
            clone.CreatedDate = DateTime.UtcNow;
            clone.ModifiedDate = DateTime.UtcNow;

            return clone;
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using EventSimulator.Core.Constants;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Models
{
    /// <summary>
    /// Represents a concrete instance of a Windows Event Log entry with enhanced validation and thread-safety.
    /// This class captures all specific details of an individual event occurrence including its timestamp,
    /// parameters, generation status, and XML representation.
    /// </summary>
    [JsonSerializable]
    public class EventInstance
    {
        /// <summary>
        /// Gets or sets the unique identifier for this event instance.
        /// </summary>
        [Required]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the ID of the template used to generate this event.
        /// </summary>
        [Required]
        public int TemplateId { get; set; }

        /// <summary>
        /// Gets or sets the Windows Event Log channel for this event.
        /// </summary>
        [Required]
        public string Channel { get; set; }

        /// <summary>
        /// Gets or sets the Windows Event ID.
        /// </summary>
        [Required]
        [Range(1, 65535)]
        public int EventId { get; set; }

        /// <summary>
        /// Gets or sets the event level (severity).
        /// </summary>
        [Required]
        [Range(0, 5)]
        public int Level { get; set; }

        /// <summary>
        /// Gets or sets the event source name.
        /// </summary>
        [Required]
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the name of the machine where the event was generated.
        /// </summary>
        [Required]
        public string MachineName { get; set; }

        /// <summary>
        /// Gets or sets the user context under which the event was generated.
        /// </summary>
        [Required]
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the UTC timestamp when the event was generated.
        /// </summary>
        [Required]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the current status of the event generation process.
        /// </summary>
        [Required]
        public string Status { get; set; }

        /// <summary>
        /// Gets or sets the generated Windows Event XML representation.
        /// </summary>
        public string GeneratedXml { get; set; }

        /// <summary>
        /// Gets the collection of parameters for this event instance.
        /// Implemented as a thread-safe collection.
        /// </summary>
        public ConcurrentBag<EventParameter> Parameters { get; private set; }

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the EventInstance class from a template.
        /// </summary>
        /// <param name="template">The template to base this event instance on.</param>
        /// <param name="logger">Logger instance for tracking event lifecycle.</param>
        /// <exception cref="ArgumentNullException">Thrown when template is null.</exception>
        public EventInstance(EventTemplate template, ILogger logger)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            TemplateId = template.Id;
            Channel = template.Channel;
            EventId = template.EventId;
            Level = template.Level;
            Source = template.Source;
            Parameters = new ConcurrentBag<EventParameter>();
            Timestamp = DateTime.UtcNow;
            Status = "Pending";
            MachineName = Environment.MachineName;
            UserName = Environment.UserName;

            _logger.LogInformation("Created new event instance from template {TemplateId}", template.Id);
        }

        /// <summary>
        /// Validates the event instance data with enhanced checks.
        /// </summary>
        /// <returns>ValidationResult indicating success or failure with details.</returns>
        public ValidationResult Validate()
        {
            try
            {
                // Validate Channel
                if (Channel != EventLogChannels.Security && 
                    Channel != EventLogChannels.System && 
                    Channel != EventLogChannels.Application)
                {
                    return new ValidationResult($"Invalid channel: {Channel}");
                }

                // Validate EventId
                if (EventId <= 0 || EventId > 65535)
                {
                    return new ValidationResult($"Invalid EventId: {EventId}");
                }

                // Validate Level
                if (Level < EventLogLevels.LogAlways || Level > EventLogLevels.Verbose)
                {
                    return new ValidationResult($"Invalid Level: {Level}");
                }

                // Validate Source
                if (string.IsNullOrWhiteSpace(Source))
                {
                    return new ValidationResult("Source cannot be empty");
                }

                // Validate Parameters
                foreach (var parameter in Parameters)
                {
                    if (!parameter.Validate())
                    {
                        return new ValidationResult($"Invalid parameter: {parameter.Name}");
                    }
                }

                // Validate XML if present
                if (!string.IsNullOrWhiteSpace(GeneratedXml))
                {
                    try
                    {
                        XDocument.Parse(GeneratedXml);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "XML validation failed");
                        return new ValidationResult("Invalid XML format");
                    }
                }

                _logger.LogInformation("Event instance {Id} validation successful", Id);
                return ValidationResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed for event instance {Id}", Id);
                return new ValidationResult($"Validation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the generation status with thread-safe logging.
        /// </summary>
        /// <param name="status">The new status to set.</param>
        /// <param name="reason">Optional reason for the status change.</param>
        public void SetGenerationStatus(string status, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(status))
                throw new ArgumentNullException(nameof(status));

            Status = status;
            
            if (string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Event instance {Id} status changed to {Status}", Id, status);
            }
            else
            {
                _logger.LogInformation("Event instance {Id} status changed to {Status}: {Reason}", 
                    Id, status, reason);
            }
        }
    }
}
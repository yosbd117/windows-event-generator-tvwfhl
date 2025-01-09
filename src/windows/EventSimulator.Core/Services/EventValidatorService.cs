using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.Core.Utils;
using EventSimulator.Core.Constants;

namespace EventSimulator.Core.Services
{
    /// <summary>
    /// Service implementation for validating Windows Event Log entries and templates.
    /// Ensures 99.9% conformance to Windows Event Log specifications through comprehensive validation.
    /// </summary>
    public class EventValidatorService : IEventValidator
    {
        private readonly ILogger<EventValidatorService> _logger;

        /// <summary>
        /// Initializes a new instance of the EventValidatorService with dependency injection.
        /// </summary>
        /// <param name="logger">Logger instance for tracking validation operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
        public EventValidatorService(ILogger<EventValidatorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("EventValidatorService initialized");
        }

        /// <summary>
        /// Validates the structure and content of an event template against Windows Event Log specifications.
        /// </summary>
        /// <param name="template">The event template to validate.</param>
        /// <returns>True if template is valid and meets all specifications, false otherwise.</returns>
        public async Task<bool> ValidateEventTemplate(EventTemplate template)
        {
            try
            {
                _logger.LogInformation("Starting template validation for template ID: {TemplateId}", template?.Id);

                if (template == null)
                {
                    _logger.LogError("Template validation failed: Template is null");
                    return false;
                }

                // Validate basic template structure
                if (string.IsNullOrWhiteSpace(template.Name) || 
                    string.IsNullOrWhiteSpace(template.Description) || 
                    string.IsNullOrWhiteSpace(template.Source))
                {
                    _logger.LogError("Template validation failed: Missing required fields");
                    return false;
                }

                // Validate channel against known constants
                if (template.Channel != EventLogChannels.Security && 
                    template.Channel != EventLogChannels.System && 
                    template.Channel != EventLogChannels.Application)
                {
                    _logger.LogError("Template validation failed: Invalid channel {Channel}", template.Channel);
                    return false;
                }

                // Validate event ID range
                if (template.EventId <= 0 || template.EventId > 65535)
                {
                    _logger.LogError("Template validation failed: Invalid EventId {EventId}", template.EventId);
                    return false;
                }

                // Validate event level
                if (template.Level < EventLogLevels.LogAlways || template.Level > EventLogLevels.Verbose)
                {
                    _logger.LogError("Template validation failed: Invalid Level {Level}", template.Level);
                    return false;
                }

                // Validate parameters if present
                if (template.Parameters != null && template.Parameters.Any())
                {
                    foreach (var parameter in template.Parameters)
                    {
                        if (!parameter.Validate())
                        {
                            _logger.LogError("Template validation failed: Invalid parameter {ParameterName}", parameter.Name);
                            return false;
                        }
                    }
                }

                _logger.LogInformation("Template validation successful for template ID: {TemplateId}", template.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Template validation failed with exception for template ID: {TemplateId}", template?.Id);
                return false;
            }
        }

        /// <summary>
        /// Validates an event instance before it is written to the Windows Event Log.
        /// </summary>
        /// <param name="eventInstance">The event instance to validate.</param>
        /// <returns>True if event instance is valid and ready for generation, false otherwise.</returns>
        public async Task<bool> ValidateEventInstance(EventInstance eventInstance)
        {
            try
            {
                _logger.LogInformation("Starting event instance validation for ID: {EventId}", eventInstance?.Id);

                if (eventInstance == null)
                {
                    _logger.LogError("Event instance validation failed: Instance is null");
                    return false;
                }

                // Validate basic instance data
                if (string.IsNullOrWhiteSpace(eventInstance.Source) || 
                    string.IsNullOrWhiteSpace(eventInstance.MachineName) || 
                    string.IsNullOrWhiteSpace(eventInstance.UserName))
                {
                    _logger.LogError("Event instance validation failed: Missing required fields");
                    return false;
                }

                // Validate channel
                if (eventInstance.Channel != EventLogChannels.Security && 
                    eventInstance.Channel != EventLogChannels.System && 
                    eventInstance.Channel != EventLogChannels.Application)
                {
                    _logger.LogError("Event instance validation failed: Invalid channel {Channel}", eventInstance.Channel);
                    return false;
                }

                // Validate event ID
                if (eventInstance.EventId <= 0 || eventInstance.EventId > 65535)
                {
                    _logger.LogError("Event instance validation failed: Invalid EventId {EventId}", eventInstance.EventId);
                    return false;
                }

                // Validate level
                if (eventInstance.Level < EventLogLevels.LogAlways || eventInstance.Level > EventLogLevels.Verbose)
                {
                    _logger.LogError("Event instance validation failed: Invalid Level {Level}", eventInstance.Level);
                    return false;
                }

                // Validate timestamp
                if (eventInstance.Timestamp == default || eventInstance.Timestamp > DateTime.UtcNow)
                {
                    _logger.LogError("Event instance validation failed: Invalid timestamp");
                    return false;
                }

                // Validate XML if present
                if (!string.IsNullOrWhiteSpace(eventInstance.GeneratedXml))
                {
                    if (!EventXmlGenerator.ValidateEventXml(eventInstance.GeneratedXml))
                    {
                        _logger.LogError("Event instance validation failed: Invalid XML structure");
                        return false;
                    }
                }

                _logger.LogInformation("Event instance validation successful for ID: {EventId}", eventInstance.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event instance validation failed with exception for ID: {EventId}", eventInstance?.Id);
                return false;
            }
        }

        /// <summary>
        /// Validates the parameters of an event against its template definition.
        /// </summary>
        /// <param name="eventInstance">The event instance containing parameters to validate.</param>
        /// <param name="template">The template defining parameter requirements.</param>
        /// <returns>True if all parameters are valid and match template specifications, false otherwise.</returns>
        public async Task<bool> ValidateEventParameters(EventInstance eventInstance, EventTemplate template)
        {
            try
            {
                _logger.LogInformation("Starting parameter validation for event ID: {EventId}, template ID: {TemplateId}", 
                    eventInstance?.Id, template?.Id);

                if (eventInstance == null || template == null)
                {
                    _logger.LogError("Parameter validation failed: Instance or template is null");
                    return false;
                }

                // Validate required parameters presence
                var requiredTemplateParams = template.Parameters.Where(p => p.IsRequired);
                foreach (var requiredParam in requiredTemplateParams)
                {
                    var instanceParam = eventInstance.Parameters.FirstOrDefault(p => p.Name == requiredParam.Name);
                    if (instanceParam == null || string.IsNullOrWhiteSpace(instanceParam.Value))
                    {
                        _logger.LogError("Parameter validation failed: Missing required parameter {ParameterName}", 
                            requiredParam.Name);
                        return false;
                    }
                }

                // Validate each parameter against template definition
                foreach (var instanceParam in eventInstance.Parameters)
                {
                    var templateParam = template.Parameters.FirstOrDefault(p => p.Name == instanceParam.Name);
                    if (templateParam == null)
                    {
                        _logger.LogError("Parameter validation failed: Unknown parameter {ParameterName}", 
                            instanceParam.Name);
                        return false;
                    }

                    // Validate parameter data type
                    if (templateParam.DataType != instanceParam.DataType)
                    {
                        _logger.LogError("Parameter validation failed: Data type mismatch for {ParameterName}", 
                            instanceParam.Name);
                        return false;
                    }

                    // Validate parameter value
                    if (!instanceParam.Validate())
                    {
                        _logger.LogError("Parameter validation failed: Invalid value for {ParameterName}", 
                            instanceParam.Name);
                        return false;
                    }
                }

                _logger.LogInformation("Parameter validation successful for event ID: {EventId}", eventInstance.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parameter validation failed with exception for event ID: {EventId}", 
                    eventInstance?.Id);
                return false;
            }
        }
    }
}
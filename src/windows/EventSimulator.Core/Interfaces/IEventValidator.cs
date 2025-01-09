using System.Threading.Tasks;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for validating Windows Event Log entries and templates.
    /// Ensures all events conform to Windows Event Log specifications and contain valid data.
    /// Implements comprehensive validation with support for async operations and detailed validation reporting.
    /// </summary>
    public interface IEventValidator
    {
        /// <summary>
        /// Validates the structure and content of an event template including its XML structure,
        /// parameters, metadata, and relationships. Ensures template conforms to Windows Event Log
        /// specifications and contains all required elements.
        /// </summary>
        /// <param name="template">The event template to validate.</param>
        /// <returns>
        /// A task that represents the asynchronous validation operation.
        /// Returns true if the template is valid according to Windows Event Log specifications
        /// and contains all required elements, false otherwise.
        /// </returns>
        /// <remarks>
        /// Validation includes:
        /// - Required field presence and format (Id, Name, Description, Channel, EventId, Level, Source)
        /// - Channel validity against EventLogChannels constants
        /// - EventId range (1-65535)
        /// - Level validity against EventLogLevels constants
        /// - Parameter definitions and relationships
        /// - MITRE ATT&CK reference format (if present)
        /// - Version format compliance
        /// </remarks>
        Task<bool> ValidateEventTemplate(EventTemplate template);

        /// <summary>
        /// Validates an event instance before it is written to the Windows Event Log,
        /// ensuring all required fields and data are present and valid. Performs comprehensive
        /// validation of event structure, content, and relationships.
        /// </summary>
        /// <param name="eventInstance">The event instance to validate.</param>
        /// <returns>
        /// A task that represents the asynchronous validation operation.
        /// Returns true if the event instance is valid and can be written to the Windows Event Log,
        /// false otherwise.
        /// </returns>
        /// <remarks>
        /// Validation includes:
        /// - Required field presence (Id, TemplateId, Channel, EventId, Level, Source)
        /// - Channel validity against EventLogChannels constants
        /// - EventId range validation
        /// - Level validity against EventLogLevels constants
        /// - Parameter completeness and validity
        /// - XML structure validation (if GeneratedXml is present)
        /// - Machine name and username validity
        /// - Timestamp validity
        /// </remarks>
        Task<bool> ValidateEventInstance(EventInstance eventInstance);

        /// <summary>
        /// Validates the parameters of an event against its template definition,
        /// ensuring all required parameters are present with correct types and values.
        /// Performs detailed parameter validation including type checking, range validation,
        /// and relationship verification.
        /// </summary>
        /// <param name="eventInstance">The event instance containing parameters to validate.</param>
        /// <param name="template">The template defining parameter requirements.</param>
        /// <returns>
        /// A task that represents the asynchronous validation operation.
        /// Returns true if all parameters are valid according to the template definition
        /// and Windows Event Log specifications, false otherwise.
        /// </returns>
        /// <remarks>
        /// Validation includes:
        /// - Parameter presence verification against template requirements
        /// - Data type validation for each parameter
        /// - Required parameter value presence
        /// - Custom validation pattern compliance
        /// - Parameter relationship integrity
        /// - Value range and format validation
        /// </remarks>
        Task<bool> ValidateEventParameters(EventInstance eventInstance, EventTemplate template);
    }
}
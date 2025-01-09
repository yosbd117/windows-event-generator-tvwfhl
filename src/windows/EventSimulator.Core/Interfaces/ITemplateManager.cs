using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Results;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for managing Windows Event Log templates with comprehensive support for
    /// template operations, MITRE ATT&CK mapping, validation, and version control.
    /// Implements enterprise-grade template management with thread safety and async operations.
    /// </summary>
    public interface ITemplateManager
    {
        /// <summary>
        /// Retrieves a specific event template by its unique identifier.
        /// </summary>
        /// <param name="templateId">The unique identifier of the template to retrieve.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the requested EventTemplate if found, or error details if not found or invalid.</returns>
        Task<Result<EventTemplate>> GetTemplateAsync(int templateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new event template with validation against Windows Event Log specifications.
        /// </summary>
        /// <param name="template">The template to create.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the created EventTemplate with assigned ID, or error details if validation fails.</returns>
        Task<Result<EventTemplate>> CreateTemplateAsync(EventTemplate template, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing event template while maintaining version history.
        /// </summary>
        /// <param name="template">The template with updated information.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the updated EventTemplate with incremented version, or error details if validation fails.</returns>
        Task<Result<EventTemplate>> UpdateTemplateAsync(EventTemplate template, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a template and its version history.
        /// </summary>
        /// <param name="templateId">The unique identifier of the template to delete.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result indicating success or failure with error details.</returns>
        Task<Result> DeleteTemplateAsync(int templateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves templates associated with a specific MITRE ATT&CK technique.
        /// </summary>
        /// <param name="techniqueId">The MITRE ATT&CK technique ID (format: T####.###).</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing matching templates or error details.</returns>
        Task<Result<IEnumerable<EventTemplate>>> GetTemplatesByMitreTechniqueAsync(string techniqueId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the version history for a specific template.
        /// </summary>
        /// <param name="templateId">The unique identifier of the template.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the template's version history ordered by version number descending.</returns>
        Task<Result<IEnumerable<EventTemplate>>> GetTemplateVersionHistoryAsync(int templateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates a collection of templates against Windows Event Log specifications and MITRE ATT&CK mapping.
        /// </summary>
        /// <param name="templates">The collection of templates to validate.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing validation results for each template.</returns>
        Task<Result<IDictionary<int, ValidationResult>>> ValidateTemplatesAsync(IEnumerable<EventTemplate> templates, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a copy of an existing template with a new identifier and reset version.
        /// </summary>
        /// <param name="sourceTemplateId">The unique identifier of the template to clone.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the cloned template or error details.</returns>
        Task<Result<EventTemplate>> CloneTemplateAsync(int sourceTemplateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Imports templates from an external source with validation and version conflict resolution.
        /// </summary>
        /// <param name="templates">The collection of templates to import.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the imported templates with assigned IDs or error details.</returns>
        Task<Result<IEnumerable<EventTemplate>>> ImportTemplatesAsync(IEnumerable<EventTemplate> templates, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports templates to a format suitable for external systems or backup.
        /// </summary>
        /// <param name="templateIds">The collection of template IDs to export.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing the exported templates or error details.</returns>
        Task<Result<IEnumerable<EventTemplate>>> ExportTemplatesAsync(IEnumerable<int> templateIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches templates based on specified criteria with pagination support.
        /// </summary>
        /// <param name="searchCriteria">The search criteria including name, description, and MITRE technique.</param>
        /// <param name="pageNumber">The page number for pagination (1-based).</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
        /// <returns>A Result containing matching templates and total count.</returns>
        Task<Result<(IEnumerable<EventTemplate> Templates, int TotalCount)>> SearchTemplatesAsync(
            TemplateSearchCriteria searchCriteria,
            int pageNumber = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents the search criteria for template queries.
    /// </summary>
    public class TemplateSearchCriteria
    {
        /// <summary>
        /// Gets or sets the name filter (supports partial matches).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the description filter (supports partial matches).
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the MITRE ATT&CK technique ID filter.
        /// </summary>
        public string MitreAttackTechnique { get; set; }

        /// <summary>
        /// Gets or sets the event channel filter.
        /// </summary>
        public string Channel { get; set; }

        /// <summary>
        /// Gets or sets the category filter.
        /// </summary>
        public string Category { get; set; }
    }

    /// <summary>
    /// Represents the validation result for a template.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Gets or sets whether the template is valid.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Gets or sets the collection of validation errors if any.
        /// </summary>
        public ICollection<string> Errors { get; set; } = new List<string>();
    }
}
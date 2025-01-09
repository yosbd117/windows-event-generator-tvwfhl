// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.Extensions.Logging v6.0.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EventSimulator.Core.Models;
using EventSimulator.Data.Context;

namespace EventSimulator.Data.Repositories
{
    /// <summary>
    /// Repository class that handles data access operations for Windows Event Log entries with support for
    /// high-performance batch operations, partitioned data access, and comprehensive monitoring.
    /// </summary>
    public class EventRepository
    {
        private readonly EventSimulatorDbContext _context;
        private readonly ILogger<EventRepository> _logger;
        private const int BatchSize = 1000;
        private readonly bool EnableChangeTracking = false;

        /// <summary>
        /// Initializes a new instance of EventRepository with dependency injection and configuration.
        /// </summary>
        /// <param name="context">Database context for event data access</param>
        /// <param name="logger">Logger instance for monitoring and diagnostics</param>
        public EventRepository(EventSimulatorDbContext context, ILogger<EventRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Configure change tracking for optimal performance
            _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            _context.ChangeTracker.AutoDetectChangesEnabled = EnableChangeTracking;
        }

        /// <summary>
        /// Retrieves a single event by its ID with optimized query performance.
        /// </summary>
        /// <param name="eventId">The ID of the event to retrieve</param>
        /// <returns>The event instance if found, null otherwise</returns>
        public async Task<EventInstance> GetEventByIdAsync(int eventId)
        {
            try
            {
                _logger.LogDebug("Retrieving event with ID: {EventId}", eventId);

                var result = await _context.Events
                    .AsNoTracking()
                    .Include(e => e.Parameters)
                    .FirstOrDefaultAsync(e => e.Id == eventId);

                if (result == null)
                {
                    _logger.LogInformation("Event with ID {EventId} not found", eventId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event with ID {EventId}", eventId);
                throw;
            }
        }

        /// <summary>
        /// Retrieves events within a specified date range using partition-aware querying.
        /// </summary>
        /// <param name="startDate">Start date of the range</param>
        /// <param name="endDate">End date of the range</param>
        /// <param name="maxResults">Maximum number of results to return</param>
        /// <returns>Collection of events within the date range</returns>
        public async Task<IEnumerable<EventInstance>> GetEventsByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            int maxResults = 1000)
        {
            try
            {
                _logger.LogDebug("Retrieving events between {StartDate} and {EndDate}", startDate, endDate);

                var query = _context.Events
                    .AsNoTracking()
                    .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
                    .Include(e => e.Parameters)
                    .OrderBy(e => e.Timestamp)
                    .Take(maxResults);

                var results = await query.ToListAsync();

                _logger.LogInformation("Retrieved {Count} events between {StartDate} and {EndDate}",
                    results.Count, startDate, endDate);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving events between {StartDate} and {EndDate}",
                    startDate, endDate);
                throw;
            }
        }

        /// <summary>
        /// Adds a new event instance with comprehensive validation and error handling.
        /// </summary>
        /// <param name="eventInstance">The event instance to add</param>
        /// <returns>The added event instance with generated ID</returns>
        public async Task<EventInstance> AddEventAsync(EventInstance eventInstance)
        {
            if (eventInstance == null)
                throw new ArgumentNullException(nameof(eventInstance));

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var validationResult = eventInstance.Validate();
                if (validationResult != ValidationResult.Success)
                {
                    throw new InvalidOperationException($"Event validation failed: {validationResult.ErrorMessage}");
                }

                _context.Events.Add(eventInstance);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Added new event with ID {EventId}", eventInstance.Id);

                return eventInstance;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error adding new event");
                throw;
            }
        }

        /// <summary>
        /// Adds multiple event instances in an optimized batch operation.
        /// </summary>
        /// <param name="events">Collection of event instances to add</param>
        /// <returns>Number of events successfully added</returns>
        public async Task<int> AddEventsAsync(IEnumerable<EventInstance> events)
        {
            if (events == null)
                throw new ArgumentNullException(nameof(events));

            var eventsList = events.ToList();
            if (!eventsList.Any())
                return 0;

            using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            try
            {
                _logger.LogInformation("Beginning batch insert of {Count} events", eventsList.Count);

                // Disable change tracking for better performance
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                var addedCount = 0;
                foreach (var batch in eventsList.Chunk(BatchSize))
                {
                    // Validate all events in batch
                    foreach (var evt in batch)
                    {
                        var validationResult = evt.Validate();
                        if (validationResult != ValidationResult.Success)
                        {
                            throw new InvalidOperationException(
                                $"Event validation failed: {validationResult.ErrorMessage}");
                        }
                    }

                    await _context.Events.AddRangeAsync(batch);
                    addedCount += await _context.SaveChangesAsync();

                    _logger.LogDebug("Added batch of {Count} events", batch.Length);
                }

                transaction.Complete();
                _logger.LogInformation("Successfully added {Count} events", addedCount);

                return addedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch event insertion");
                throw;
            }
            finally
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = EnableChangeTracking;
            }
        }

        /// <summary>
        /// Updates the status of an event instance with optimistic concurrency.
        /// </summary>
        /// <param name="eventId">ID of the event to update</param>
        /// <param name="status">New status value</param>
        /// <returns>True if update successful, false if event not found</returns>
        public async Task<bool> UpdateEventStatusAsync(int eventId, string status)
        {
            try
            {
                var eventInstance = await _context.Events.FindAsync(eventId);
                if (eventInstance == null)
                {
                    _logger.LogWarning("Event with ID {EventId} not found for status update", eventId);
                    return false;
                }

                eventInstance.SetGenerationStatus(status);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Updated status to {Status} for event {EventId}", status, eventId);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency conflict updating status for event {EventId}", eventId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for event {EventId}", eventId);
                throw;
            }
        }

        /// <summary>
        /// Removes events older than the specified retention period.
        /// </summary>
        /// <param name="retentionDays">Number of days to retain events</param>
        /// <returns>Number of events removed</returns>
        public async Task<int> RemoveOldEventsAsync(int retentionDays)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
                
                _logger.LogInformation("Removing events older than {CutoffDate}", cutoffDate);

                var result = await _context.Events
                    .Where(e => e.Timestamp < cutoffDate)
                    .ExecuteDeleteAsync();

                _logger.LogInformation("Removed {Count} events older than {CutoffDate}", 
                    result, cutoffDate);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing old events");
                throw;
            }
        }
    }
}
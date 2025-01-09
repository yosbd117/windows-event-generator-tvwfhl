using System;
using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.Logging;
using EventSimulator.Core.Constants;

namespace EventSimulator.Common.Extensions
{
    /// <summary>
    /// Provides extension methods for Windows Event Log operations with comprehensive validation
    /// and error handling. These methods ensure accurate event level mapping, channel validation,
    /// and user-friendly display formatting.
    /// </summary>
    public static class EventLogExtensions
    {
        private static readonly ILogger _logger = LoggerFactory.Create(builder => 
            builder.AddConsole()).CreateLogger(typeof(EventLogExtensions));

        /// <summary>
        /// Converts an integer event level to the corresponding EventLevel enumeration value.
        /// Provides comprehensive validation and error handling with detailed logging.
        /// </summary>
        /// <param name="level">The integer level to convert (valid range: 0-5)</param>
        /// <returns>The corresponding EventLevel value, defaults to Information if mapping fails</returns>
        public static EventLevel ToEventLevel(this int level)
        {
            _logger.LogDebug("Converting integer level {Level} to EventLevel", level);

            if (level < EventLogLevels.LogAlways || level > EventLogLevels.Verbose)
            {
                _logger.LogWarning("Invalid event level {Level}. Must be between {Min} and {Max}. Defaulting to Information.",
                    level, EventLogLevels.LogAlways, EventLogLevels.Verbose);
                return EventLevel.Information;
            }

            try
            {
                var eventLevel = level switch
                {
                    EventLogLevels.LogAlways => EventLevel.LogAlways,
                    EventLogLevels.Critical => EventLevel.Critical,
                    EventLogLevels.Error => EventLevel.Error,
                    EventLogLevels.Warning => EventLevel.Warning,
                    EventLogLevels.Information => EventLevel.Information,
                    EventLogLevels.Verbose => EventLevel.Verbose,
                    _ => EventLevel.Information
                };

                _logger.LogDebug("Successfully mapped level {Level} to EventLevel {EventLevel}", 
                    level, eventLevel);
                return eventLevel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting level {Level} to EventLevel. Defaulting to Information.", 
                    level);
                return EventLevel.Information;
            }
        }

        /// <summary>
        /// Validates if the provided channel name is a valid Windows Event Log channel.
        /// Performs comprehensive validation against known channels and system configuration.
        /// </summary>
        /// <param name="channelName">The name of the channel to validate</param>
        /// <returns>True if the channel is valid, false otherwise</returns>
        public static bool IsValidEventChannel(this string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                _logger.LogWarning("Channel name is null or empty");
                return false;
            }

            _logger.LogDebug("Validating event channel: {ChannelName}", channelName);

            try
            {
                // First check against known standard channels
                bool isStandardChannel = channelName switch
                {
                    EventLogChannels.Security => true,
                    EventLogChannels.System => true,
                    EventLogChannels.Application => true,
                    _ => false
                };

                if (isStandardChannel)
                {
                    _logger.LogDebug("Channel {ChannelName} validated as standard channel", channelName);
                    return true;
                }

                // For non-standard channels, verify existence in Windows Event Log
                using var eventLogSession = new EventLogSession();
                var channelExists = eventLogSession.GetLogNames()
                    .Contains(channelName, StringComparer.OrdinalIgnoreCase);

                _logger.LogDebug("Channel {ChannelName} existence check result: {Exists}", 
                    channelName, channelExists);
                return channelExists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating channel {ChannelName}", channelName);
                return false;
            }
        }

        /// <summary>
        /// Converts an EventLevel to its user-friendly display string with culture awareness.
        /// Provides consistent and localized representation of event levels.
        /// </summary>
        /// <param name="level">The EventLevel to convert</param>
        /// <returns>A localized, user-friendly display string for the event level</returns>
        public static string ToDisplayString(this EventLevel level)
        {
            _logger.LogDebug("Converting EventLevel {Level} to display string", level);

            try
            {
                var displayString = level switch
                {
                    EventLevel.LogAlways => "Log Always",
                    EventLevel.Critical => "Critical",
                    EventLevel.Error => "Error",
                    EventLevel.Warning => "Warning",
                    EventLevel.Information => "Information",
                    EventLevel.Verbose => "Verbose",
                    _ => "Unknown Level"
                };

                _logger.LogDebug("Successfully converted EventLevel {Level} to display string: {DisplayString}", 
                    level, displayString);
                return displayString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting EventLevel {Level} to display string. Returning 'Unknown Level'.", 
                    level);
                return "Unknown Level";
            }
        }
    }
}
// System v6.0.0 - Basic .NET functionality for constant definitions and access modifiers
using System;

namespace EventSimulator.Core.Constants
{
    /// <summary>
    /// Provides standardized Windows Event Log level constants that exactly match Windows specifications.
    /// This sealed static class ensures consistent event level assignment across the application.
    /// </summary>
    public static sealed class EventLogLevels
    {
        /// <summary>
        /// Specifies that the event should always be logged regardless of the configured logging level.
        /// This is the highest priority level in the Windows Event Log system.
        /// </summary>
        public const int LogAlways = 0;

        /// <summary>
        /// Indicates a critical error that has caused a major failure in the system.
        /// These events require immediate attention and typically indicate system failure.
        /// </summary>
        public const int Critical = 1;

        /// <summary>
        /// Represents an error condition that impacts system functionality but may not require immediate attention.
        /// These events indicate significant problems.
        /// </summary>
        public const int Error = 2;

        /// <summary>
        /// Indicates a potentially harmful situation that may require attention.
        /// These events highlight conditions that could lead to future problems.
        /// </summary>
        public const int Warning = 3;

        /// <summary>
        /// Represents informational messages that highlight normal system operation and provide operational tracking.
        /// </summary>
        public const int Information = 4;

        /// <summary>
        /// Provides detailed debug-level information useful for troubleshooting.
        /// These events contain the most detailed level of system operation information.
        /// </summary>
        public const int Verbose = 5;

        /// <summary>
        /// Private constructor to prevent instantiation as this is a static constants class.
        /// </summary>
#pragma warning disable IDE0051 // Remove unused private member
        private EventLogLevels()
        {
            // Private constructor to prevent instantiation
        }
#pragma warning restore IDE0051
    }
}
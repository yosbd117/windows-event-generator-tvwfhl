using System;

namespace EventSimulator.Core.Constants
{
    /// <summary>
    /// Provides constant definitions for standard Windows Event Log channels.
    /// These constants ensure consistent and type-safe references to Windows Event Log channels
    /// throughout the application, preventing runtime errors due to typos or incorrect channel names.
    /// </summary>
    public static class EventLogChannels
    {
        /// <summary>
        /// Represents the Windows Application event log channel.
        /// Contains events from applications and software installed on the system.
        /// </summary>
        public const string Application = "Application";

        /// <summary>
        /// Represents the Windows Forwarded Events log channel.
        /// Contains events that have been forwarded from other computers.
        /// </summary>
        public const string ForwardedEvents = "ForwardedEvents";

        /// <summary>
        /// Represents the Windows Security event log channel.
        /// Contains security-related events such as valid and invalid logon attempts,
        /// resource access, and security policy changes.
        /// </summary>
        public const string Security = "Security";

        /// <summary>
        /// Represents the Windows Setup event log channel.
        /// Contains events related to Windows setup and upgrades.
        /// </summary>
        public const string Setup = "Setup";

        /// <summary>
        /// Represents the Windows System event log channel.
        /// Contains events logged by system components, drivers, and services.
        /// </summary>
        public const string System = "System";
    }
}
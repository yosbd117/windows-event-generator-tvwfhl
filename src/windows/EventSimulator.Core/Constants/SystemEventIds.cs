using System;
using System.ComponentModel;

namespace EventSimulator.Core.Constants
{
    /// <summary>
    /// Static class containing constant definitions for standard Windows System Event IDs,
    /// organized by functional categories and mapped to MITRE ATT&CK techniques.
    /// </summary>
    [System.Runtime.CompilerServices.CompilerGenerated]
    public static class SystemEventIds
    {
        #region Service Control Events (MITRE ATT&CK: T1543)

        /// <summary>
        /// Service Control Manager: Service start event
        /// MITRE ATT&CK: T1543 - Create or Modify System Process
        /// </summary>
        [Description("Service Control Manager: Service start event")]
        public const int ServiceStart = 7000;

        /// <summary>
        /// Service Control Manager: Service stop event
        /// MITRE ATT&CK: T1543 - Create or Modify System Process
        /// </summary>
        [Description("Service Control Manager: Service stop event")]
        public const int ServiceStop = 7001;

        /// <summary>
        /// Service Control Manager: Service terminated unexpectedly
        /// MITRE ATT&CK: T1529 - System Shutdown/Reboot
        /// </summary>
        [Description("Service Control Manager: Service terminated unexpectedly")]
        public const int ServiceCrash = 7031;

        #endregion

        #region System Operation Events (MITRE ATT&CK: T1529)

        /// <summary>
        /// EventLog: System startup event
        /// MITRE ATT&CK: T1529 - System Shutdown/Reboot
        /// </summary>
        [Description("EventLog: System startup event")]
        public const int SystemStartup = 6005;

        /// <summary>
        /// EventLog: System shutdown event
        /// MITRE ATT&CK: T1529 - System Shutdown/Reboot
        /// </summary>
        [Description("EventLog: System shutdown event")]
        public const int SystemShutdown = 6006;

        /// <summary>
        /// EventLog: System restart event
        /// MITRE ATT&CK: T1529 - System Shutdown/Reboot
        /// </summary>
        [Description("EventLog: System restart event")]
        public const int SystemRestart = 6008;

        /// <summary>
        /// Kernel-General: System time change
        /// MITRE ATT&CK: T1070 - Indicator Removal on Host
        /// </summary>
        [Description("Kernel-General: System time change")]
        public const int TimeChange = 1;

        #endregion

        #region Hardware Error Events (MITRE ATT&CK: T1499)

        /// <summary>
        /// Disk: Physical disk error detected
        /// MITRE ATT&CK: T1499 - Endpoint Denial of Service
        /// </summary>
        [Description("Disk: Physical disk error detected")]
        public const int DiskError = 7;

        #endregion

        #region Network Operation Events (MITRE ATT&CK: T1110)

        /// <summary>
        /// TCP/IP: Network connectivity lost
        /// MITRE ATT&CK: T1110 - Brute Force
        /// </summary>
        [Description("TCP/IP: Network connectivity lost")]
        public const int NetworkError = 4201;

        #endregion

        #region Driver Operation Events (MITRE ATT&CK: T1543.003)

        /// <summary>
        /// Driver Management: Driver loaded successfully
        /// MITRE ATT&CK: T1543.003 - Windows Service
        /// </summary>
        [Description("Driver Management: Driver loaded successfully")]
        public const int DriverLoad = 10;

        /// <summary>
        /// Driver Management: Driver load failure
        /// MITRE ATT&CK: T1543.003 - Windows Service
        /// </summary>
        [Description("Driver Management: Driver load failure")]
        public const int DriverError = 219;

        #endregion

        #region Application Error Events (MITRE ATT&CK: T1499)

        /// <summary>
        /// Application Error: Application termination error
        /// MITRE ATT&CK: T1499 - Endpoint Denial of Service
        /// </summary>
        [Description("Application Error: Application termination error")]
        public const int ApplicationCrash = 1000;

        #endregion

        /// <summary>
        /// Private constructor to prevent instantiation of static class
        /// </summary>
        private SystemEventIds()
        {
            // Private constructor to prevent instantiation
        }
    }
}
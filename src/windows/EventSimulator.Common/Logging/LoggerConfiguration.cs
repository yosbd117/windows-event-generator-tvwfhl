using Microsoft.Extensions.Logging; // v6.0.0
using Serilog; // v2.12.0
using Serilog.Events; // v2.12.0
using Serilog.Sinks.File; // v5.0.0
using Serilog.Sinks.EventLog; // v3.1.0
using Serilog.Sinks.ApplicationInsights; // v4.0.0
using Serilog.Enrichers.Thread; // v3.1.0
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using EventSimulator.Common.Configuration;

namespace EventSimulator.Common.Logging
{
    /// <summary>
    /// Configures and manages high-performance logging settings with comprehensive security monitoring capabilities.
    /// Supports multiple sinks including buffered file system, Windows Event Log, and Application Insights.
    /// </summary>
    public class LoggerConfiguration
    {
        private const string DEFAULT_LOG_PREFIX = "EventSimulator";
        private const string SECURITY_EVENT_SOURCE = "Windows Event Simulator";
        private const int DEFAULT_BUFFER_SIZE = 1000;
        private const int DEFAULT_BATCH_SIZE = 100;
        private const int DEFAULT_MAX_FILE_SIZE = 104857600; // 100 MB
        private const int DEFAULT_RETENTION_DAYS = 90;

        public string LogFilePath { get; private set; }
        public string ApplicationInsightsKey { get; private set; }
        public LogEventLevel MinimumLevel { get; private set; }
        public bool EnableConsoleLogging { get; private set; }
        public bool EnableFileLogging { get; private set; }
        public bool EnableEventLogLogging { get; private set; }
        public bool EnableApplicationInsights { get; private set; }
        public bool EnableBuffering { get; private set; }
        public int BufferSize { get; private set; }
        public int RetentionDays { get; private set; }
        public string SecurityEventSource { get; private set; }
        public string LogFilePrefix { get; private set; }
        public bool EnableEncryption { get; private set; }
        public int MaximumFileSize { get; private set; }
        public int BatchSize { get; private set; }

        /// <summary>
        /// Initializes a new instance of LoggerConfiguration with optimized settings for high-performance logging.
        /// </summary>
        /// <param name="appSettings">Application configuration settings.</param>
        public LoggerConfiguration(AppSettings appSettings)
        {
            if (appSettings == null)
                throw new ArgumentNullException(nameof(appSettings));

            // Initialize log file path with date-based naming
            LogFilePrefix = DEFAULT_LOG_PREFIX;
            LogFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Logs",
                $"{LogFilePrefix}_.log"
            );

            // Configure environment-specific settings
            MinimumLevel = appSettings.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
                ? LogEventLevel.Information
                : LogEventLevel.Debug;

            // Configure performance settings
            EnableBuffering = true;
            BufferSize = DEFAULT_BUFFER_SIZE;
            BatchSize = DEFAULT_BATCH_SIZE;
            MaximumFileSize = DEFAULT_MAX_FILE_SIZE;
            RetentionDays = DEFAULT_RETENTION_DAYS;

            // Configure security monitoring
            SecurityEventSource = SECURITY_EVENT_SOURCE;
            EnableEncryption = appSettings.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase);

            // Configure logging targets
            EnableConsoleLogging = appSettings.EnableDetailedLogging;
            EnableFileLogging = true;
            EnableEventLogLogging = true;
            EnableApplicationInsights = !string.IsNullOrEmpty(ApplicationInsightsKey);
        }

        /// <summary>
        /// Configures Serilog logger with optimized sinks and security monitoring.
        /// </summary>
        /// <returns>Configured high-performance Serilog logger instance.</returns>
        public ILogger ConfigureLogger()
        {
            var loggerConfig = new Serilog.LoggerConfiguration()
                .MinimumLevel.Is(MinimumLevel)
                .Enrich.WithThreadId()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName();

            // Configure buffered file sink with retry policy
            if (EnableFileLogging && ValidateLogPath())
            {
                loggerConfig.WriteTo.File(
                    LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetentionDays,
                    buffered: EnableBuffering,
                    bufferSize: BufferSize,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    fileSizeLimitBytes: MaximumFileSize,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    encoding: System.Text.Encoding.UTF8
                );
            }

            // Configure Windows Event Log sink with security categories
            if (EnableEventLogLogging && ValidateEventLogAccess())
            {
                loggerConfig.WriteTo.EventLog(
                    SecurityEventSource,
                    manageEventSource: true,
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    batchPostingLimit: BatchSize
                );
            }

            // Configure Application Insights sink with sampling
            if (EnableApplicationInsights)
            {
                loggerConfig.WriteTo.ApplicationInsights(
                    ApplicationInsightsKey,
                    TelemetryConverter.Traces,
                    LogEventLevel.Information
                );
            }

            // Configure console logging for development
            if (EnableConsoleLogging)
            {
                loggerConfig.WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                );
            }

            return loggerConfig.CreateLogger();
        }

        /// <summary>
        /// Validates log directory permissions and creates if necessary.
        /// </summary>
        /// <returns>True if validation successful.</returns>
        private bool ValidateLogPath()
        {
            try
            {
                string logDirectory = Path.GetDirectoryName(LogFilePath);
                if (!Directory.Exists(logDirectory))
                {
                    DirectoryInfo di = Directory.CreateDirectory(logDirectory);
                    DirectorySecurity ds = di.GetAccessControl();

                    // Set restrictive permissions
                    var adminRule = new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow
                    );

                    var systemRule = new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow
                    );

                    ds.AddAccessRule(adminRule);
                    ds.AddAccessRule(systemRule);
                    di.SetAccessControl(ds);
                }

                // Verify write access
                using (File.Create(
                    Path.Combine(logDirectory, $"test_{Guid.NewGuid()}.tmp"),
                    1,
                    FileOptions.DeleteOnClose
                ))
                { }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Verifies Windows Event Log access permissions.
        /// </summary>
        /// <returns>True if access verified.</returns>
        private bool ValidateEventLogAccess()
        {
            try
            {
                if (!System.Diagnostics.EventLog.SourceExists(SecurityEventSource))
                {
                    System.Diagnostics.EventLog.CreateEventSource(
                        SecurityEventSource,
                        "Application"
                    );
                }

                // Verify write access
                using (var eventLog = new System.Diagnostics.EventLog("Application"))
                {
                    eventLog.Source = SecurityEventSource;
                    eventLog.WriteEntry(
                        "Event Log access validation successful",
                        System.Diagnostics.EventLogEntryType.Information,
                        1000
                    );
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
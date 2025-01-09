using Microsoft.Extensions.Configuration; // v6.0.0
using System;
using System.IO;
using EventSimulator.Common.Configuration;

namespace EventSimulator.Common.Configuration
{
    /// <summary>
    /// Configuration class that manages global application settings with comprehensive validation,
    /// security controls, and performance optimization features for the Windows Event Simulator.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Gets or sets the application name.
        /// </summary>
        public string ApplicationName { get; set; }

        /// <summary>
        /// Gets or sets the application version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the environment (Development/Staging/Production).
        /// </summary>
        public string Environment { get; set; }

        /// <summary>
        /// Gets or sets the database configuration settings.
        /// </summary>
        public DatabaseSettings Database { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of concurrent events that can be generated.
        /// </summary>
        public int MaxConcurrentEvents { get; set; }

        /// <summary>
        /// Gets or sets the batch size for event generation.
        /// </summary>
        public int EventGenerationBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the interval between event generation batches.
        /// </summary>
        public TimeSpan EventGenerationInterval { get; set; }

        /// <summary>
        /// Gets or sets whether detailed logging is enabled.
        /// </summary>
        public bool EnableDetailedLogging { get; set; }

        /// <summary>
        /// Gets or sets whether performance monitoring is enabled.
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; }

        /// <summary>
        /// Gets or sets the path for storing event templates.
        /// </summary>
        public string TemplateStoragePath { get; set; }

        /// <summary>
        /// Gets or sets the cache expiration time in minutes.
        /// </summary>
        public int CacheExpirationMinutes { get; set; }

        /// <summary>
        /// Gets or sets whether Windows authentication is enabled.
        /// </summary>
        public bool UseWindowsAuthentication { get; set; }

        /// <summary>
        /// Initializes a new instance of AppSettings with secure defaults.
        /// </summary>
        public AppSettings()
        {
            ApplicationName = "Windows Event Simulator";
            Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            Environment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            
            Database = new DatabaseSettings();
            
            // Performance defaults based on technical specifications
            MaxConcurrentEvents = 1000;
            EventGenerationBatchSize = 100;
            EventGenerationInterval = TimeSpan.FromSeconds(1);
            
            // Environment-specific defaults
            EnableDetailedLogging = Environment.Equals("Development", StringComparison.OrdinalIgnoreCase);
            EnablePerformanceMonitoring = true;
            
            // Security-aware defaults
            TemplateStoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
            CacheExpirationMinutes = Environment.Equals("Production", StringComparison.OrdinalIgnoreCase) ? 60 : 15;
            UseWindowsAuthentication = true;
        }

        /// <summary>
        /// Loads and validates application configuration with environment overrides and security checks.
        /// </summary>
        /// <param name="configuration">The configuration source.</param>
        /// <exception cref="ConfigurationException">Thrown when configuration is invalid or incomplete.</exception>
        public void LoadConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            try
            {
                // Bind configuration section
                configuration.GetSection("AppSettings").Bind(this);

                // Load database configuration
                Database.LoadConfiguration(configuration);

                // Apply environment-specific overrides
                ApplyEnvironmentOverrides();

                // Validate configuration
                if (!Validate())
                {
                    throw new ConfigurationException("Application configuration validation failed");
                }

                // Ensure template storage directory exists
                EnsureTemplateStorageExists();

                // Initialize performance monitoring if enabled
                if (EnablePerformanceMonitoring)
                {
                    InitializePerformanceMonitoring();
                }
            }
            catch (Exception ex) when (ex is not ConfigurationException)
            {
                throw new ConfigurationException("Failed to load application configuration", ex);
            }
        }

        /// <summary>
        /// Performs comprehensive validation of configuration settings.
        /// </summary>
        /// <returns>True if configuration is valid and secure.</returns>
        public bool Validate()
        {
            // Validate application identity
            if (string.IsNullOrEmpty(ApplicationName) || string.IsNullOrEmpty(Version))
                return false;

            // Validate environment
            if (!new[] { "Development", "Staging", "Production" }.Contains(Environment))
                return false;

            // Validate performance settings
            if (MaxConcurrentEvents < 1 || MaxConcurrentEvents > 10000)
                return false;

            if (EventGenerationBatchSize < 1 || EventGenerationBatchSize > MaxConcurrentEvents)
                return false;

            if (EventGenerationInterval < TimeSpan.FromMilliseconds(100) || 
                EventGenerationInterval > TimeSpan.FromSeconds(60))
                return false;

            // Validate cache settings
            if (CacheExpirationMinutes < 1 || CacheExpirationMinutes > 1440)
                return false;

            // Validate template storage
            if (string.IsNullOrEmpty(TemplateStoragePath) || 
                Path.GetFullPath(TemplateStoragePath).Length > 260)
                return false;

            // Validate database configuration
            if (!Database.Validate())
                return false;

            return true;
        }

        private void ApplyEnvironmentOverrides()
        {
            // Apply environment-specific security policies
            if (Environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
            {
                EnableDetailedLogging = false;
                UseWindowsAuthentication = true;
                CacheExpirationMinutes = Math.Max(CacheExpirationMinutes, 60);
            }

            // Apply environment variables if present
            var maxEvents = Environment.GetEnvironmentVariable("EVENT_SIMULATOR_MAX_CONCURRENT_EVENTS");
            if (!string.IsNullOrEmpty(maxEvents) && int.TryParse(maxEvents, out int maxEventsValue))
            {
                MaxConcurrentEvents = Math.Min(maxEventsValue, 10000);
            }

            var batchSize = Environment.GetEnvironmentVariable("EVENT_SIMULATOR_BATCH_SIZE");
            if (!string.IsNullOrEmpty(batchSize) && int.TryParse(batchSize, out int batchSizeValue))
            {
                EventGenerationBatchSize = Math.Min(batchSizeValue, MaxConcurrentEvents);
            }
        }

        private void EnsureTemplateStorageExists()
        {
            if (!Directory.Exists(TemplateStoragePath))
            {
                try
                {
                    Directory.CreateDirectory(TemplateStoragePath);
                }
                catch (Exception ex)
                {
                    throw new ConfigurationException($"Failed to create template storage directory: {ex.Message}", ex);
                }
            }
        }

        private void InitializePerformanceMonitoring()
        {
            // Initialize performance counters and monitoring
            try
            {
                System.Diagnostics.PerformanceCounter.CategoryExists("Windows Event Simulator");
                // Additional performance monitoring initialization would go here
            }
            catch (Exception ex)
            {
                EnablePerformanceMonitoring = false;
                throw new ConfigurationException("Failed to initialize performance monitoring", ex);
            }
        }
    }
}
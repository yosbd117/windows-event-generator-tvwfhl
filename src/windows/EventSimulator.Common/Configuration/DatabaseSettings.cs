using Microsoft.Extensions.Configuration; // v6.0.0
using System;
using System.Data.SqlClient;
using System.Text.RegularExpressions;

namespace EventSimulator.Common.Configuration
{
    /// <summary>
    /// Configuration class that manages database connection settings, security parameters, and performance optimization options
    /// for the Windows Event Simulator database connectivity.
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>
        /// Gets or sets the complete connection string for SQL Server database access.
        /// If not explicitly set, will be built from individual properties.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the SQL Server instance name or address.
        /// </summary>
        public string Server { get; set; }

        /// <summary>
        /// Gets or sets the target database name.
        /// </summary>
        public string Database { get; set; }

        /// <summary>
        /// Gets or sets the SQL Server authentication user ID.
        /// Only used when IntegratedSecurity is false.
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Gets or sets the SQL Server authentication password.
        /// Only used when IntegratedSecurity is false.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets whether to use Windows Authentication (true) or SQL Server Authentication (false).
        /// </summary>
        public bool IntegratedSecurity { get; set; }

        /// <summary>
        /// Gets or sets the timeout in seconds for database commands.
        /// </summary>
        public int CommandTimeout { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of connection retry attempts.
        /// </summary>
        public int MaxRetryCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum size of the connection pool.
        /// </summary>
        public int MaxPoolSize { get; set; }

        /// <summary>
        /// Gets or sets whether table partitioning is enabled for scalability.
        /// </summary>
        public bool EnablePartitioning { get; set; }

        /// <summary>
        /// Initializes a new instance of DatabaseSettings with secure default values.
        /// </summary>
        public DatabaseSettings()
        {
            // Set secure defaults
            CommandTimeout = 30;
            MaxRetryCount = 3;
            MaxPoolSize = 100;
            IntegratedSecurity = true;
            EnablePartitioning = true;
            
            // Initialize empty strings for security
            Server = string.Empty;
            Database = string.Empty;
            UserId = string.Empty;
            Password = string.Empty;
            ConnectionString = string.Empty;
        }

        /// <summary>
        /// Loads and validates database configuration from the provided configuration source.
        /// </summary>
        /// <param name="configuration">The configuration source containing database settings.</param>
        /// <exception cref="ConfigurationException">Thrown when configuration is invalid or incomplete.</exception>
        public void LoadConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            try
            {
                // Bind configuration section
                configuration.GetSection("DatabaseSettings").Bind(this);

                // If connection string is not provided, build it from components
                if (string.IsNullOrEmpty(ConnectionString))
                {
                    ConnectionString = BuildConnectionString();
                }

                // Validate configuration
                if (!Validate())
                {
                    throw new ConfigurationException("Database configuration validation failed");
                }
            }
            catch (Exception ex) when (ex is not ConfigurationException)
            {
                throw new ConfigurationException("Failed to load database configuration", ex);
            }
        }

        /// <summary>
        /// Constructs a secure SQL Server connection string using configured properties.
        /// </summary>
        /// <returns>A secure SQL Server connection string.</returns>
        public string BuildConnectionString()
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = Server,
                InitialCatalog = Database,
                IntegratedSecurity = IntegratedSecurity,
                
                // Security settings
                Encrypt = true,
                TrustServerCertificate = false,
                
                // Performance settings
                MaxPoolSize = MaxPoolSize,
                ConnectRetryCount = MaxRetryCount,
                ConnectRetryInterval = 10, // 10 seconds between retries
                
                // Command timeout
                ConnectTimeout = CommandTimeout,
                
                // Additional security measures
                PersistSecurityInfo = false,
                MultipleActiveResultSets = true
            };

            // Add SQL Authentication credentials if not using Windows Authentication
            if (!IntegratedSecurity)
            {
                builder.UserID = UserId;
                builder.Password = Password;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Performs comprehensive validation of all database configuration settings.
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise.</returns>
        public bool Validate()
        {
            // Server validation
            if (string.IsNullOrEmpty(Server))
                return false;

            // Database name validation
            if (string.IsNullOrEmpty(Database) || !Regex.IsMatch(Database, @"^[\w\-\.]+$"))
                return false;

            // Timeout validation
            if (CommandTimeout < 0 || CommandTimeout > 3600)
                return false;

            // Retry count validation
            if (MaxRetryCount < 0 || MaxRetryCount > 10)
                return false;

            // Pool size validation
            if (MaxPoolSize < 1 || MaxPoolSize > 1000)
                return false;

            // Credential validation for SQL Authentication
            if (!IntegratedSecurity)
            {
                if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Password))
                    return false;
            }

            // Connection string security validation
            try
            {
                var testBuilder = new SqlConnectionStringBuilder(
                    string.IsNullOrEmpty(ConnectionString) ? BuildConnectionString() : ConnectionString
                );

                // Verify required security settings
                if (!testBuilder.Encrypt)
                    return false;

                if (testBuilder.PersistSecurityInfo)
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Exception thrown when database configuration is invalid or cannot be loaded.
    /// </summary>
    public class ConfigurationException : Exception
    {
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
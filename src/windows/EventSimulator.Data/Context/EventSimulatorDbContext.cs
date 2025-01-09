// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.Extensions.Options v6.0.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using EventSimulator.Common.Configuration;
using EventSimulator.Core.Models;
using EventSimulator.Data.Configurations;

namespace EventSimulator.Data.Context
{
    /// <summary>
    /// Entity Framework Core DbContext for managing database operations, entity configurations,
    /// and performance optimizations with support for table partitioning and comprehensive error handling.
    /// </summary>
    public class EventSimulatorDbContext : DbContext
    {
        private readonly DatabaseSettings _settings;
        private readonly ILogger<EventSimulatorDbContext> _logger;

        /// <summary>
        /// Gets or sets the DbSet for managing Windows Event instances.
        /// </summary>
        public DbSet<EventInstance> Events { get; set; }

        /// <summary>
        /// Gets or sets the DbSet for managing event templates.
        /// </summary>
        public DbSet<EventTemplate> Templates { get; set; }

        /// <summary>
        /// Gets or sets the DbSet for managing scenario definitions.
        /// </summary>
        public DbSet<ScenarioDefinition> Scenarios { get; set; }

        /// <summary>
        /// Gets or sets the DbSet for managing scenario events.
        /// </summary>
        public DbSet<ScenarioEvent> ScenarioEvents { get; set; }

        /// <summary>
        /// Initializes a new instance of EventSimulatorDbContext with database settings and logging capabilities.
        /// </summary>
        /// <param name="settings">Database configuration settings</param>
        /// <param name="options">DbContext options</param>
        /// <param name="logger">Logger instance</param>
        public EventSimulatorDbContext(
            IOptions<DatabaseSettings> settings,
            DbContextOptions<EventSimulatorDbContext> options,
            ILogger<EventSimulatorDbContext> logger)
            : base(options)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("Initializing EventSimulatorDbContext with connection to {Server}/{Database}",
                _settings.Server, _settings.Database);

            // Configure command timeout from settings
            Database.SetCommandTimeout(TimeSpan.FromSeconds(_settings.CommandTimeout));

            // Set up database connection monitoring
            Database.GetConnectionString();
            ChangeTracker.StateChanged += (sender, e) => 
                _logger.LogDebug("Entity {EntityType} state changed from {OldState} to {NewState}",
                    e.Entry.Entity.GetType().Name, e.OldState, e.NewState);
        }

        /// <summary>
        /// Configures the database model with comprehensive entity configurations and performance optimizations.
        /// </summary>
        /// <param name="modelBuilder">The model builder instance</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
                throw new ArgumentNullException(nameof(modelBuilder));

            try
            {
                _logger.LogInformation("Configuring database model");

                // Apply entity configurations
                modelBuilder.ApplyConfiguration(new EventConfiguration());
                modelBuilder.ApplyConfiguration(new TemplateConfiguration());
                modelBuilder.ApplyConfiguration(new ScenarioConfiguration());

                // Configure global query filters for soft delete
                modelBuilder.Entity<EventInstance>()
                    .HasQueryFilter(e => e.Status != "Deleted");
                modelBuilder.Entity<ScenarioDefinition>()
                    .HasQueryFilter(s => s.IsActive);

                // Configure default value generators
                foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                {
                    // Add created/modified date tracking
                    if (entityType.FindProperty("CreatedDate") != null)
                    {
                        modelBuilder.Entity(entityType.Name)
                            .Property("CreatedDate")
                            .HasDefaultValueSql("GETUTCDATE()");
                    }
                    if (entityType.FindProperty("ModifiedDate") != null)
                    {
                        modelBuilder.Entity(entityType.Name)
                            .Property("ModifiedDate")
                            .HasDefaultValueSql("GETUTCDATE()");
                    }
                }

                // Configure table partitioning if enabled
                if (_settings.EnablePartitioning)
                {
                    _logger.LogInformation("Configuring table partitioning");
                    ConfigureTablePartitioning(modelBuilder);
                }

                base.OnModelCreating(modelBuilder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring database model");
                throw;
            }
        }

        /// <summary>
        /// Configures the database context with comprehensive options for performance and reliability.
        /// </summary>
        /// <param name="optionsBuilder">The options builder instance</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder == null)
                throw new ArgumentNullException(nameof(optionsBuilder));

            try
            {
                if (!optionsBuilder.IsConfigured)
                {
                    _logger.LogInformation("Configuring database context options");

                    optionsBuilder
                        .UseSqlServer(_settings.ConnectionString, sqlOptions =>
                        {
                            // Configure connection resiliency
                            sqlOptions.EnableRetryOnFailure(
                                maxRetryCount: _settings.MaxRetryCount,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorNumbersToAdd: null);

                            // Configure execution strategy
                            sqlOptions.ExecutionStrategy(dependencies =>
                                new SqlServerRetryingExecutionStrategy(
                                    dependencies,
                                    maxRetryCount: _settings.MaxRetryCount,
                                    maxRetryDelay: TimeSpan.FromSeconds(30)));

                            // Configure connection pooling
                            sqlOptions.MaxBatchSize(100);
                            sqlOptions.CommandTimeout(_settings.CommandTimeout);
                        })
                        .EnableDetailedErrors()
                        .EnableSensitiveDataLogging(false)
                        .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);

                    // Configure logging
                    optionsBuilder.LogTo(message => _logger.LogDebug(message),
                        new[] { DbLoggerCategory.Database.Command.Name });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring database context");
                throw;
            }
        }

        /// <summary>
        /// Configures table partitioning for scalable data management.
        /// </summary>
        /// <param name="modelBuilder">The model builder instance</param>
        private void ConfigureTablePartitioning(ModelBuilder modelBuilder)
        {
            // Configure Events table partitioning by date
            modelBuilder.Entity<EventInstance>()
                .ToTable(table => table.HasPartitioning(
                    "RANGE RIGHT FOR VALUES (N'2023-01-01', N'2024-01-01', N'2025-01-01')"));

            // Configure Templates table partitioning by category
            modelBuilder.Entity<EventTemplate>()
                .ToTable(table => table.HasPartitioning(
                    "LIST FOR VALUES (N'Security', N'System', N'Application')"));

            // Configure Scenarios table partitioning by status
            modelBuilder.Entity<ScenarioDefinition>()
                .ToTable(table => table.HasPartitioning(
                    "LIST FOR VALUES (N'Active', N'Archived', N'Draft')"));
        }
    }
}
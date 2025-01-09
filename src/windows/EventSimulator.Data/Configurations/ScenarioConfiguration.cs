// External package versions:
// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.EntityFrameworkCore.Metadata.Builders v6.0.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EventSimulator.Core.Models;

namespace EventSimulator.Data.Configurations
{
    /// <summary>
    /// Configures the Entity Framework Core mapping for ScenarioDefinition entities,
    /// including relationships, constraints, and optimized indexing strategies.
    /// </summary>
    public class ScenarioConfiguration : IEntityTypeConfiguration<ScenarioDefinition>
    {
        /// <summary>
        /// Configures the database schema for ScenarioDefinition entities with comprehensive
        /// indexing and optimization support for efficient querying and scaling.
        /// </summary>
        /// <param name="builder">The entity type builder for ScenarioDefinition</param>
        public void Configure(EntityTypeBuilder<ScenarioDefinition> builder)
        {
            // Table configuration
            builder.ToTable("Scenarios", schema: "dbo")
                .HasComment("Stores scenario definitions for event generation");

            // Primary key
            builder.HasKey(s => s.ScenarioId)
                .IsClustered();

            builder.Property(s => s.ScenarioId)
                .UseIdentityColumn()
                .IsRequired()
                .HasComment("Unique identifier for the scenario");

            // Required properties
            builder.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(200)
                .IsUnicode(true)
                .HasComment("Name of the scenario");

            builder.Property(s => s.Description)
                .IsRequired()
                .HasMaxLength(1000)
                .IsUnicode(true)
                .HasComment("Detailed description of the scenario");

            builder.Property(s => s.Category)
                .IsRequired()
                .HasMaxLength(100)
                .IsUnicode(true)
                .HasComment("Category for organizational grouping");

            // Optional properties
            builder.Property(s => s.MitreAttackReference)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasComment("MITRE ATT&CK technique reference");

            builder.Property(s => s.Version)
                .IsRequired()
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasComment("Version of the scenario definition")
                .IsConcurrencyToken(); // Optimistic concurrency control

            // JSON configuration storage with compression
            builder.Property(s => s.Configuration)
                .HasColumnType("nvarchar(max)")
                .HasCompressionEnabled()
                .HasComment("JSON configuration for scenario-specific settings");

            // Status and timestamps
            builder.Property(s => s.IsActive)
                .IsRequired()
                .HasDefaultValue(true)
                .HasComment("Indicates if the scenario is active");

            builder.Property(s => s.CreatedDate)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()")
                .HasComment("UTC timestamp of scenario creation");

            builder.Property(s => s.ModifiedDate)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()")
                .HasComment("UTC timestamp of last modification");

            // Relationships
            builder.HasMany(s => s.Events)
                .WithOne()
                .HasForeignKey(e => e.ScenarioId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasComment("One-to-many relationship with scenario events");

            // Indexes for efficient querying
            builder.HasIndex(s => new { s.Name, s.Category })
                .HasDatabaseName("IX_Scenarios_Name_Category")
                .IsUnique()
                .HasFilter("[IsActive] = 1")
                .HasFillFactor(90)
                .HasComment("Unique index for active scenarios by name and category");

            builder.HasIndex(s => s.IsActive)
                .HasDatabaseName("IX_Scenarios_IsActive")
                .HasFilter("[IsActive] = 1")
                .HasFillFactor(90)
                .HasComment("Filtered index for active scenarios");

            builder.HasIndex(s => s.MitreAttackReference)
                .HasDatabaseName("IX_Scenarios_MitreAttackReference")
                .HasFilter("[MitreAttackReference] IS NOT NULL")
                .HasFillFactor(90)
                .HasComment("Index for MITRE ATT&CK reference queries");

            builder.HasIndex(s => s.ModifiedDate)
                .HasDatabaseName("IX_Scenarios_ModifiedDate")
                .HasFillFactor(90)
                .HasComment("Index for temporal queries");

            // Table partitioning by ModifiedDate for efficient data management
            builder.HasPartitioningScheme(s => s.ModifiedDate)
                .HasPartitionFunction("MonthlyPartition")
                .HasComment("Monthly partitioning by ModifiedDate");

            // Check constraints
            builder.HasCheckConstraint(
                "CK_Scenarios_MitreAttackReference",
                "[MitreAttackReference] IS NULL OR [MitreAttackReference] LIKE 'T[0-9][0-9][0-9][0-9]%'")
                .HasComment("Ensures MITRE ATT&CK reference format");

            builder.HasCheckConstraint(
                "CK_Scenarios_Version",
                "[Version] LIKE '[0-9]%.[0-9]%'")
                .HasComment("Ensures version format compliance");
        }
    }
}
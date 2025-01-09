// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.EntityFrameworkCore.Metadata.Builders v6.0.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EventSimulator.Core.Models;

namespace EventSimulator.Data.Configurations
{
    /// <summary>
    /// Provides comprehensive Entity Framework Core configuration for the EventInstance entity,
    /// implementing advanced database optimization strategies including partitioning,
    /// indexing, compression, and monitoring capabilities.
    /// </summary>
    public class EventConfiguration : IEntityTypeConfiguration<EventInstance>
    {
        /// <summary>
        /// Configures the database schema, relationships, and optimization strategies for EventInstance.
        /// </summary>
        /// <param name="builder">The entity type builder for EventInstance configuration.</param>
        public void Configure(EntityTypeBuilder<EventInstance> builder)
        {
            // Table configuration
            builder.ToTable("Events", "dbo", table =>
            {
                table.HasComment("Stores Windows Event Log entries with comprehensive auditing");
                table.IsTemporal(); // Enable temporal tables for change tracking
            });

            // Primary Key
            builder.HasKey(e => e.Id)
                  .IsClustered();

            builder.Property(e => e.Id)
                  .UseIdentityColumn()
                  .HasComment("Unique identifier for the event instance");

            // Required properties with constraints
            builder.Property(e => e.TemplateId)
                  .IsRequired()
                  .HasComment("Foreign key reference to the event template");

            builder.Property(e => e.Channel)
                  .IsRequired()
                  .HasMaxLength(50)
                  .HasComment("Windows Event Log channel (Security, System, Application)");

            builder.Property(e => e.EventId)
                  .IsRequired()
                  .HasComment("Windows Event ID (1-65535)");

            builder.Property(e => e.Level)
                  .IsRequired()
                  .HasComment("Event severity level (0-5)");

            builder.Property(e => e.Source)
                  .IsRequired()
                  .HasMaxLength(255)
                  .HasComment("Event source identifier");

            builder.Property(e => e.MachineName)
                  .IsRequired()
                  .HasMaxLength(255)
                  .HasComment("Name of the machine where event was generated");

            builder.Property(e => e.UserName)
                  .IsRequired()
                  .HasMaxLength(255)
                  .HasComment("User context for event generation");

            // Timestamp configuration with high precision
            builder.Property(e => e.Timestamp)
                  .IsRequired()
                  .HasColumnType("datetime2(7)")
                  .HasComment("UTC timestamp of event generation");

            builder.Property(e => e.Status)
                  .IsRequired()
                  .HasMaxLength(50)
                  .HasComment("Current status of event generation process");

            // XML storage with compression
            builder.Property(e => e.GeneratedXml)
                  .HasColumnType("nvarchar(max)")
                  .IsUnicode(true)
                  .HasComment("Generated Windows Event XML representation");

            // Configure JSON storage for Parameters
            builder.Property(e => e.Parameters)
                  .HasColumnType("nvarchar(max)")
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Concurrent.ConcurrentBag<EventParameter>>(v, null)
                  )
                  .HasComment("Event parameters stored as JSON");

            // Partitioning scheme
            builder.HasPartitioningScheme("PS_Events_Monthly")
                  .HasPartitionFunction("PF_Events_Monthly")
                  .HasPartitionKey(e => e.Timestamp)
                  .HasDataCompression(DataCompressionType.Page);

            // Indexes for performance optimization
            builder.HasIndex(e => new { e.Timestamp, e.Status })
                  .HasDatabaseName("IX_Events_Timestamp_Status")
                  .IncludeProperties(e => new { e.EventId, e.Source })
                  .HasFillFactor(90);

            builder.HasIndex(e => e.Status)
                  .HasDatabaseName("IX_Events_Status")
                  .HasFilter("[Status] = 'Active'")
                  .HasFillFactor(90);

            builder.HasIndex(e => e.TemplateId)
                  .HasDatabaseName("IX_Events_TemplateId");

            // Audit columns
            builder.Property<DateTime>("CreatedAt")
                  .HasDefaultValueSql("GETUTCDATE()")
                  .HasComment("UTC timestamp of record creation");

            builder.Property<string>("CreatedBy")
                  .HasMaxLength(255)
                  .HasDefaultValueSql("SYSTEM_USER")
                  .HasComment("User who created the record");

            builder.Property<DateTime>("ModifiedAt")
                  .HasDefaultValueSql("GETUTCDATE()")
                  .HasComment("UTC timestamp of last modification");

            builder.Property<string>("ModifiedBy")
                  .HasMaxLength(255)
                  .HasDefaultValueSql("SYSTEM_USER")
                  .HasComment("User who last modified the record");

            // Concurrency token
            builder.Property<byte[]>("RowVersion")
                  .IsRowVersion()
                  .HasComment("Concurrency control version");

            // Query filters
            builder.HasQueryFilter(e => e.Status != "Deleted");

            // Relationships
            builder.HasOne<EventTemplate>()
                  .WithMany()
                  .HasForeignKey(e => e.TemplateId)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
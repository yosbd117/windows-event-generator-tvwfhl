using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EventSimulator.Core.Models;

namespace EventSimulator.Data.Configurations
{
    /// <summary>
    /// Entity Framework Core configuration class that defines the database schema, relationships,
    /// and constraints for the EventTemplate entity. Implements enterprise-grade database design
    /// with proper indexing, constraints, and performance optimizations.
    /// </summary>
    public class TemplateConfiguration : IEntityTypeConfiguration<EventTemplate>
    {
        /// <summary>
        /// Configures the database schema for EventTemplate entity including table structure,
        /// constraints, indexes, and relationships using EF Core's fluent API.
        /// </summary>
        /// <param name="builder">The entity type builder for EventTemplate</param>
        public void Configure(EntityTypeBuilder<EventTemplate> builder)
        {
            // Table configuration
            builder.ToTable("Templates", tb => tb.IsTemporal()); // Enable temporal tables for audit history

            // Primary Key
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id)
                  .UseIdentityColumn()
                  .IsRequired();

            // Required properties with constraints
            builder.Property(t => t.Name)
                  .IsRequired()
                  .HasMaxLength(200)
                  .IsUnicode(true);

            builder.Property(t => t.Description)
                  .IsRequired()
                  .HasMaxLength(1000)
                  .IsUnicode(true);

            builder.Property(t => t.Channel)
                  .IsRequired()
                  .HasMaxLength(50)
                  .IsUnicode(false); // ASCII sufficient for channel names

            builder.Property(t => t.EventId)
                  .IsRequired();

            builder.Property(t => t.Level)
                  .IsRequired()
                  .HasComment("Event level (0=LogAlways, 1=Critical, 2=Error, 3=Warning, 4=Information, 5=Verbose)");

            builder.Property(t => t.Source)
                  .IsRequired()
                  .HasMaxLength(100)
                  .IsUnicode(true);

            // Optional properties with constraints
            builder.Property(t => t.Category)
                  .HasMaxLength(100)
                  .IsUnicode(true);

            builder.Property(t => t.MitreAttackTechnique)
                  .HasMaxLength(50)
                  .IsUnicode(false); // ASCII sufficient for MITRE IDs

            // Version tracking
            builder.Property(t => t.Version)
                  .IsRequired()
                  .HasMaxLength(20)
                  .IsUnicode(false) // ASCII sufficient for version numbers
                  .IsConcurrencyToken(); // Optimistic concurrency control

            // Timestamps
            builder.Property(t => t.CreatedDate)
                  .IsRequired()
                  .HasDefaultValueSql("GETUTCDATE()");

            builder.Property(t => t.ModifiedDate)
                  .IsRequired()
                  .HasDefaultValueSql("GETUTCDATE()")
                  .ValueGeneratedOnAddOrUpdate();

            // Parameters JSON column
            builder.Property(t => t.Parameters)
                  .IsRequired()
                  .HasColumnType("nvarchar(max)")
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.ICollection<EventParameter>>(v, null)
                  );

            // Indexes for performance optimization
            builder.HasIndex(t => t.Name)
                  .HasDatabaseName("IX_Templates_Name");

            builder.HasIndex(t => t.Category)
                  .HasDatabaseName("IX_Templates_Category")
                  .HasFilter("[Category] IS NOT NULL");

            builder.HasIndex(t => t.MitreAttackTechnique)
                  .HasDatabaseName("IX_Templates_MitreAttack")
                  .HasFilter("[MitreAttackTechnique] IS NOT NULL");

            builder.HasIndex(t => new { t.Channel, t.EventId })
                  .HasDatabaseName("IX_Templates_Channel_EventId");

            // Check constraints
            builder.ToTable(t => t.HasCheckConstraint("CK_Templates_EventId", "[EventId] > 0 AND [EventId] <= 65535"));
            builder.ToTable(t => t.HasCheckConstraint("CK_Templates_Level", "[Level] >= 0 AND [Level] <= 5"));
            builder.ToTable(t => t.HasCheckConstraint("CK_Templates_Version", "[Version] LIKE '_._._'"));
        }
    }
}
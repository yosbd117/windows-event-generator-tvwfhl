using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EventSimulator.Data.Migrations
{
    /// <summary>
    /// Initial database migration that creates the schema with advanced features like table partitioning,
    /// optimized indexes, and performance configurations for the Windows Event Simulator.
    /// </summary>
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create partition function and scheme for EventInstances table
            migrationBuilder.Sql(@"
                CREATE PARTITION FUNCTION [PF_EventInstancesByDate](datetime2)
                AS RANGE RIGHT FOR VALUES (
                    '2024-01-01', '2024-02-01', '2024-03-01', '2024-04-01',
                    '2024-05-01', '2024-06-01', '2024-07-01', '2024-08-01',
                    '2024-09-01', '2024-10-01', '2024-11-01', '2024-12-01'
                );

                CREATE PARTITION SCHEME [PS_EventInstancesByDate]
                AS PARTITION [PF_EventInstancesByDate]
                ALL TO ([PRIMARY]);
            ");

            // Create EventTemplates table
            migrationBuilder.CreateTable(
                name: "EventTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Description = table.Column<string>(nullable: false),
                    Channel = table.Column<string>(maxLength: 50, nullable: false),
                    EventId = table.Column<int>(nullable: false),
                    Level = table.Column<int>(nullable: false),
                    Source = table.Column<string>(maxLength: 100, nullable: false),
                    Category = table.Column<string>(maxLength: 100, nullable: true),
                    MitreAttackTechnique = table.Column<string>(maxLength: 20, nullable: true),
                    Version = table.Column<string>(maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(nullable: false),
                    ModifiedDate = table.Column<DateTime>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTemplates", x => x.Id);
                });

            // Create EventInstances table with partitioning
            migrationBuilder.CreateTable(
                name: "EventInstances",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(nullable: false),
                    Channel = table.Column<string>(maxLength: 50, nullable: false),
                    EventId = table.Column<int>(nullable: false),
                    Level = table.Column<int>(nullable: false),
                    Source = table.Column<string>(maxLength: 100, nullable: false),
                    MachineName = table.Column<string>(maxLength: 100, nullable: false),
                    UserName = table.Column<string>(maxLength: 100, nullable: false),
                    Timestamp = table.Column<DateTime>(nullable: false),
                    Status = table.Column<string>(maxLength: 50, nullable: false),
                    GeneratedXml = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventInstances", x => x.Id)
                        .Annotation("SqlServer:Clustered", true);
                    table.ForeignKey(
                        name: "FK_EventInstances_EventTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EventTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:PartitionScheme", "PS_EventInstancesByDate")
                .Annotation("SqlServer:PartitionColumn", "Timestamp");

            // Create EventParameters table
            migrationBuilder.CreateTable(
                name: "EventParameters",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventInstanceId = table.Column<int>(nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    Value = table.Column<string>(nullable: true),
                    DataType = table.Column<string>(maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventParameters_EventInstances_EventInstanceId",
                        column: x => x.EventInstanceId,
                        principalTable: "EventInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create ScenarioDefinitions table
            migrationBuilder.CreateTable(
                name: "ScenarioDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Description = table.Column<string>(nullable: false),
                    Category = table.Column<string>(maxLength: 100, nullable: false),
                    MitreAttackReference = table.Column<string>(maxLength: 20, nullable: true),
                    Version = table.Column<string>(maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(nullable: false),
                    ModifiedDate = table.Column<DateTime>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    Configuration = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioDefinitions", x => x.Id);
                });

            // Create ScenarioEvents table
            migrationBuilder.CreateTable(
                name: "ScenarioEvents",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScenarioId = table.Column<int>(nullable: false),
                    TemplateId = table.Column<int>(nullable: false),
                    Sequence = table.Column<int>(nullable: false),
                    DelayMilliseconds = table.Column<int>(nullable: false),
                    Parameters = table.Column<string>(nullable: true),
                    DependsOnEvents = table.Column<string>(nullable: true),
                    Conditions = table.Column<string>(nullable: true),
                    IsEnabled = table.Column<bool>(nullable: false),
                    LastExecutionTime = table.Column<DateTime>(nullable: true),
                    ExecutionSucceeded = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScenarioEvents_ScenarioDefinitions_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "ScenarioDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScenarioEvents_EventTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EventTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Create optimized indexes
            migrationBuilder.CreateIndex(
                name: "IX_EventTemplates_Name",
                table: "EventTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventTemplates_IsActive",
                table: "EventTemplates",
                column: "IsActive")
                .Annotation("SqlServer:FilterDefinition", "([IsActive] = 1)");

            migrationBuilder.CreateIndex(
                name: "IX_EventInstances_Timestamp",
                table: "EventInstances",
                column: "Timestamp")
                .Annotation("SqlServer:Include", new[] { "EventId", "Source", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_EventInstances_TemplateId",
                table: "EventInstances",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_EventParameters_EventInstanceId",
                table: "EventParameters",
                column: "EventInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioDefinitions_Name",
                table: "ScenarioDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioDefinitions_IsActive",
                table: "ScenarioDefinitions",
                column: "IsActive")
                .Annotation("SqlServer:FilterDefinition", "([IsActive] = 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioEvents_ScenarioId_Sequence",
                table: "ScenarioEvents",
                columns: new[] { "ScenarioId", "Sequence" },
                unique: true);

            // Configure performance optimizations
            migrationBuilder.Sql(@"
                ALTER DATABASE CURRENT
                SET AUTO_UPDATE_STATISTICS ON;

                -- Enable page compression for historical partitions
                ALTER TABLE EventInstances
                REBUILD PARTITION = ALL
                WITH (DATA_COMPRESSION = PAGE);

                -- Create partition maintenance stored procedure
                CREATE PROCEDURE [dbo].[SP_MaintainEventPartitions]
                AS
                BEGIN
                    DECLARE @MaxPartitionDate datetime2 = DATEADD(MONTH, 3, GETDATE());
                    DECLARE @SQL nvarchar(max);
                    
                    SET @SQL = N'ALTER PARTITION SCHEME [PS_EventInstancesByDate] 
                               NEXT USED [PRIMARY];
                               
                               ALTER PARTITION FUNCTION [PF_EventInstancesByDate]()
                               SPLIT RANGE (@MaxPartitionDate);';
                    
                    EXEC sp_executesql @SQL, N'@MaxPartitionDate datetime2', @MaxPartitionDate;
                END;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[SP_MaintainEventPartitions]");

            migrationBuilder.DropTable(name: "ScenarioEvents");
            migrationBuilder.DropTable(name: "ScenarioDefinitions");
            migrationBuilder.DropTable(name: "EventParameters");
            migrationBuilder.DropTable(name: "EventInstances");
            migrationBuilder.DropTable(name: "EventTemplates");

            migrationBuilder.Sql(@"
                DROP PARTITION SCHEME [PS_EventInstancesByDate];
                DROP PARTITION FUNCTION [PF_EventInstancesByDate];
            ");
        }
    }
}
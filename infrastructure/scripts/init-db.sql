-- Windows Event Simulator Database Initialization Script
-- Version: 1.0.0
-- SQL Server 2019+

USE master;
GO

-- Create database with optimized settings
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'EventSimulator')
BEGIN
    CREATE DATABASE EventSimulator
    ON PRIMARY 
    (
        NAME = N'EventSimulator',
        FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\EventSimulator.mdf',
        SIZE = 512MB,
        MAXSIZE = UNLIMITED,
        FILEGROWTH = 256MB
    ),
    FILEGROUP [ARCHIVE] 
    (
        NAME = N'EventSimulator_Archive',
        FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\EventSimulator_Archive.ndf',
        SIZE = 1GB,
        MAXSIZE = UNLIMITED,
        FILEGROWTH = 512MB
    )
    LOG ON
    (
        NAME = N'EventSimulator_log',
        FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\EventSimulator_log.ldf',
        SIZE = 256MB,
        MAXSIZE = UNLIMITED,
        FILEGROWTH = 128MB
    );
END
GO

USE EventSimulator;
GO

-- Configure database options for optimal performance
ALTER DATABASE EventSimulator SET RECOVERY SIMPLE;
ALTER DATABASE EventSimulator SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE EventSimulator SET AUTO_UPDATE_STATISTICS ON;
ALTER DATABASE EventSimulator SET READ_COMMITTED_SNAPSHOT ON;
GO

-- Create partition function and scheme for event data
CREATE PARTITION FUNCTION [PF_EventDate](datetime2) AS RANGE RIGHT FOR VALUES (
    '2024-01-01', '2024-02-01', '2024-03-01', '2024-04-01',
    '2024-05-01', '2024-06-01', '2024-07-01', '2024-08-01',
    '2024-09-01', '2024-10-01', '2024-11-01', '2024-12-01'
);
GO

CREATE PARTITION SCHEME [PS_EventDate] AS PARTITION [PF_EventDate] 
ALL TO ([PRIMARY]);
GO

-- Create Templates table
CREATE TABLE [dbo].[Templates]
(
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Name] [nvarchar](200) NOT NULL,
    [Description] [nvarchar](1000) NULL,
    [Channel] [nvarchar](50) NOT NULL,
    [EventId] [int] NOT NULL,
    [Level] [int] NOT NULL,
    [Source] [nvarchar](100) NOT NULL,
    [Category] [nvarchar](100) NULL,
    [MitreAttackTechnique] [nvarchar](50) NULL,
    [Parameters] [nvarchar](max) NULL,
    [Version] [nvarchar](20) NOT NULL DEFAULT '1.0.0',
    [IsActive] [bit] NOT NULL DEFAULT 1,
    [CreatedDate] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedDate] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] [nvarchar](100) NOT NULL,
    [ModifiedBy] [nvarchar](100) NOT NULL,
    CONSTRAINT [PK_Templates] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UK_Templates_Name] UNIQUE NONCLUSTERED ([Name] ASC),
    CONSTRAINT [CHK_Templates_EventId] CHECK ([EventId] > 0 AND [EventId] <= 65535),
    CONSTRAINT [CHK_Templates_Level] CHECK ([Level] >= 0 AND [Level] <= 5),
    CONSTRAINT [CHK_Templates_Version] CHECK ([Version] LIKE '_._._')
);
GO

-- Create Events table with partitioning
CREATE TABLE [dbo].[Events]
(
    [Id] [bigint] IDENTITY(1,1) NOT NULL,
    [TemplateId] [int] NOT NULL,
    [EventId] [int] NOT NULL,
    [Channel] [nvarchar](50) NOT NULL,
    [Source] [nvarchar](100) NOT NULL,
    [Level] [int] NOT NULL,
    [GeneratedDate] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [Parameters] [nvarchar](max) NULL,
    [Status] [nvarchar](20) NOT NULL DEFAULT 'Pending',
    [ComputerName] [nvarchar](100) NOT NULL,
    [CorrelationId] [uniqueidentifier] NULL,
    CONSTRAINT [PK_Events] PRIMARY KEY CLUSTERED 
    (
        [GeneratedDate] ASC,
        [Id] ASC
    ) ON [PS_EventDate]([GeneratedDate])
);
GO

-- Create Scenarios table
CREATE TABLE [dbo].[Scenarios]
(
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Name] [nvarchar](200) NOT NULL,
    [Description] [nvarchar](1000) NULL,
    [Configuration] [nvarchar](max) NOT NULL,
    [IsActive] [bit] NOT NULL DEFAULT 1,
    [CreatedDate] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedDate] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] [nvarchar](100) NOT NULL,
    [ModifiedBy] [nvarchar](100) NOT NULL,
    CONSTRAINT [PK_Scenarios] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UK_Scenarios_Name] UNIQUE NONCLUSTERED ([Name] ASC)
);
GO

-- Create audit tables for change tracking
CREATE TABLE [dbo].[TemplateAudit]
(
    [AuditId] [bigint] IDENTITY(1,1) NOT NULL,
    [TemplateId] [int] NOT NULL,
    [Action] [nvarchar](10) NOT NULL,
    [Field] [nvarchar](100) NOT NULL,
    [OldValue] [nvarchar](max) NULL,
    [NewValue] [nvarchar](max) NULL,
    [ModifiedDate] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedBy] [nvarchar](100) NOT NULL,
    CONSTRAINT [PK_TemplateAudit] PRIMARY KEY CLUSTERED ([AuditId] ASC)
);
GO

-- Create indexes for performance optimization
CREATE NONCLUSTERED INDEX [IX_Templates_Category] ON [dbo].[Templates]
(
    [Category] ASC,
    [IsActive] ASC
)
INCLUDE ([Name], [EventId], [Source]);
GO

CREATE NONCLUSTERED INDEX [IX_Templates_MitreAttackTechnique] ON [dbo].[Templates]
(
    [MitreAttackTechnique] ASC
)
INCLUDE ([Name], [EventId], [Description]);
GO

CREATE NONCLUSTERED INDEX [IX_Events_Status] ON [dbo].[Events]
(
    [Status] ASC,
    [GeneratedDate] ASC
)
INCLUDE ([TemplateId], [EventId], [Channel]);
GO

CREATE NONCLUSTERED INDEX [IX_Events_CorrelationId] ON [dbo].[Events]
(
    [CorrelationId] ASC
)
WHERE [CorrelationId] IS NOT NULL;
GO

-- Create foreign key constraints
ALTER TABLE [dbo].[Events] WITH CHECK ADD CONSTRAINT [FK_Events_Templates] 
    FOREIGN KEY([TemplateId]) REFERENCES [dbo].[Templates] ([Id]);
GO

-- Create stored procedures for maintenance
CREATE PROCEDURE [dbo].[usp_MaintainPartitions]
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CurrentDate datetime2 = GETUTCDATE();
    DECLARE @NextPartitionBoundary datetime2 = DATEADD(MONTH, 1, 
        DATEADD(MONTH, DATEDIFF(MONTH, 0, @CurrentDate), 0));
    
    -- Add new partition if needed
    ALTER PARTITION SCHEME [PS_EventDate] NEXT USED [PRIMARY];
    ALTER PARTITION FUNCTION [PF_EventDate]() 
        SPLIT RANGE (@NextPartitionBoundary);
    
    -- Archive old partitions (older than 90 days)
    -- Implementation depends on archival strategy
END;
GO

-- Create roles and permissions
CREATE ROLE [EventSimulator_Reader];
CREATE ROLE [EventSimulator_Generator];
CREATE ROLE [EventSimulator_Admin];
GO

GRANT SELECT ON SCHEMA::[dbo] TO [EventSimulator_Reader];
GRANT SELECT, INSERT, UPDATE ON [dbo].[Events] TO [EventSimulator_Generator];
GRANT SELECT, INSERT, UPDATE ON [dbo].[Templates] TO [EventSimulator_Generator];
GRANT CONTROL ON SCHEMA::[dbo] TO [EventSimulator_Admin];
GO

-- Seed initial data from template files
INSERT INTO [dbo].[Templates] 
    ([Name], [Description], [Channel], [EventId], [Level], [Source], 
     [Category], [MitreAttackTechnique], [Parameters], [CreatedBy], [ModifiedBy])
SELECT 
    'SuccessfulLogon',
    'An account was successfully logged on to the system',
    'Security',
    4624,
    4,
    'Microsoft-Windows-Security-Auditing',
    'Authentication',
    'T1078',
    N'{"subjectUserSid":"required","subjectUserName":"required","logonType":"required","workstationName":"required","ipAddress":"required"}',
    'SYSTEM',
    'SYSTEM';
GO

-- Create maintenance jobs
DECLARE @JobName nvarchar(100) = N'EventSimulator_PartitionMaintenance';
IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
BEGIN
    EXEC msdb.dbo.sp_add_job
        @job_name = @JobName,
        @enabled = 1,
        @description = N'Maintains partitions for the EventSimulator database';
    
    EXEC msdb.dbo.sp_add_jobstep
        @job_name = @JobName,
        @step_name = N'Execute Maintenance',
        @subsystem = N'TSQL',
        @command = N'EXEC [dbo].[usp_MaintainPartitions]';
    
    EXEC msdb.dbo.sp_add_jobschedule
        @job_name = @JobName,
        @name = N'Daily_Midnight',
        @freq_type = 4,
        @freq_interval = 1,
        @active_start_time = 000000;
END;
GO
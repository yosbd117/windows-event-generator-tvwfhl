#Requires -Version 7.2
#Requires -Modules @{ModuleName='SqlServer'; ModuleVersion='160.0'}, @{ModuleName='Microsoft.PowerShell.Configuration'; ModuleVersion='7.2.0'}
#Requires -RunAsAdministrator

# Script Version: 1.0.0
# Author: Windows Event Simulator Team
# Last Modified: 2024

# Global Configuration
$script:DEFAULT_BACKUP_PATH = "$env:ProgramData\EventSimulator\Backups"
$script:BACKUP_RETENTION_DAYS = 90
$script:LOG_BACKUP_INTERVAL = 15
$script:ERROR_LOG_PATH = "$env:ProgramData\EventSimulator\Logs\backup-restore.log"

# Import required assemblies
Add-Type -Path "$PSScriptRoot\..\..\src\windows\EventSimulator.Common\bin\Release\net6.0\EventSimulator.Common.dll"

function Write-EventSimulatorLog {
    param(
        [string]$Message,
        [ValidateSet('Information', 'Warning', 'Error')]
        [string]$Level = 'Information'
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Add-Content -Path $ERROR_LOG_PATH -Value $logMessage
    
    switch ($Level) {
        'Warning' { Write-Warning $Message }
        'Error' { Write-Error $Message }
        default { Write-Verbose $Message }
    }
}

function Initialize-BackupEnvironment {
    param(
        [string]$BackupPath = $DEFAULT_BACKUP_PATH
    )
    
    try {
        # Create backup directory if it doesn't exist
        if (-not (Test-Path -Path $BackupPath)) {
            New-Item -Path $BackupPath -ItemType Directory -Force | Out-Null
        }

        # Set appropriate NTFS permissions
        $acl = Get-Acl -Path $BackupPath
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            "NT SERVICE\MSSQLSERVER", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
        )
        $acl.AddAccessRule($rule)
        Set-Acl -Path $BackupPath -AclObject $acl

        Write-EventSimulatorLog "Backup environment initialized successfully at $BackupPath"
        return $true
    }
    catch {
        Write-EventSimulatorLog "Failed to initialize backup environment: $_" -Level Error
        return $false
    }
}

function Backup-EventSimulatorDatabase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Full', 'Differential', 'Log')]
        [string]$BackupType,
        
        [Parameter()]
        [string]$BackupPath = $DEFAULT_BACKUP_PATH,
        
        [Parameter()]
        [bool]$Compress = $true,
        
        [Parameter()]
        [bool]$Encrypt = $true,
        
        [Parameter()]
        [string]$CertificateThumbprint
    )

    try {
        # Load database settings
        $dbSettings = [EventSimulator.Common.Configuration.DatabaseSettings]::new()
        $configPath = Join-Path $PSScriptRoot "..\..\appsettings.json"
        $config = Get-Content $configPath | ConvertFrom-Json
        $dbSettings.LoadConfiguration($config)

        # Initialize backup environment
        if (-not (Initialize-BackupEnvironment -BackupPath $BackupPath)) {
            throw "Failed to initialize backup environment"
        }

        # Generate backup filename
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupFile = Join-Path $BackupPath "EventSimulator_$($dbSettings.Database)_${BackupType}_${timestamp}.bak"

        # Prepare backup command
        $backup = New-Object Microsoft.SqlServer.Management.Smo.Backup
        $backup.Database = $dbSettings.Database
        $backup.Action = switch ($BackupType) {
            'Full' { 'Database' }
            'Differential' { 'Database' }
            'Log' { 'Log' }
        }
        $backup.Incremental = ($BackupType -eq 'Differential')
        $backup.Devices.AddDevice($backupFile, 'File')
        
        # Configure compression if available
        if ($Compress) {
            $backup.CompressionOption = 1 # Enable compression
        }

        # Configure encryption if requested
        if ($Encrypt) {
            if (-not $CertificateThumbprint) {
                throw "Certificate thumbprint is required for encrypted backups"
            }
            $backup.EncryptionOption = New-Object Microsoft.SqlServer.Management.Smo.BackupEncryptionOptions
            $backup.EncryptionOption.Algorithm = 'Aes256'
            $backup.EncryptionOption.EncryptorType = 'ServerCertificate'
            $backup.EncryptionOption.EncryptorName = $CertificateThumbprint
        }

        # Execute backup
        Write-EventSimulatorLog "Starting $BackupType backup of $($dbSettings.Database)"
        $backup.SqlBackup((New-Object Microsoft.SqlServer.Management.Smo.Server($dbSettings.Server)))

        # Verify backup
        $verify = New-Object Microsoft.SqlServer.Management.Smo.Restore
        $verify.Devices.AddDevice($backupFile, 'File')
        $verify.VerifyOnly = $true
        $verify.SqlVerify((New-Object Microsoft.SqlServer.Management.Smo.Server($dbSettings.Server)))

        # Implement retention policy
        $retentionDate = (Get-Date).AddDays(-$BACKUP_RETENTION_DAYS)
        Get-ChildItem -Path $BackupPath -Filter "EventSimulator_*.bak" | 
            Where-Object { $_.LastWriteTime -lt $retentionDate } |
            Remove-Item -Force

        Write-EventSimulatorLog "Backup completed successfully: $backupFile"
        return $true
    }
    catch {
        Write-EventSimulatorLog "Backup failed: $_" -Level Error
        return $false
    }
}

function Restore-EventSimulatorDatabase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$BackupFile,
        
        [Parameter()]
        [string]$TargetDatabase,
        
        [Parameter()]
        [bool]$OverwriteExisting = $false,
        
        [Parameter()]
        [string]$CertificateThumbprint
    )

    try {
        # Load database settings
        $dbSettings = [EventSimulator.Common.Configuration.DatabaseSettings]::new()
        $configPath = Join-Path $PSScriptRoot "..\..\appsettings.json"
        $config = Get-Content $configPath | ConvertFrom-Json
        $dbSettings.LoadConfiguration($config)

        # Validate backup file
        if (-not (Test-Path $BackupFile)) {
            throw "Backup file not found: $BackupFile"
        }

        # Set target database name
        $targetDb = if ($TargetDatabase) { $TargetDatabase } else { $dbSettings.Database }

        # Initialize restore operation
        $restore = New-Object Microsoft.SqlServer.Management.Smo.Restore
        $restore.Database = $targetDb
        $restore.Devices.AddDevice($BackupFile, 'File')
        $restore.ReplaceDatabase = $OverwriteExisting
        
        # Get SQL Server instance
        $server = New-Object Microsoft.SqlServer.Management.Smo.Server($dbSettings.Server)

        # Handle existing connections if overwriting
        if ($OverwriteExisting -and ($server.Databases[$targetDb])) {
            $server.KillAllProcesses($targetDb)
            $server.Databases[$targetDb].SetOffline()
        }

        # Configure file locations
        $dataPath = $server.Information.MasterDBPath
        $logPath = $server.Information.MasterDBLogPath
        
        $restore.RelocateFiles.Clear()
        $fileList = $restore.ReadFileList($server)
        foreach ($file in $fileList) {
            $relocate = New-Object Microsoft.SqlServer.Management.Smo.RelocateFile
            $relocate.LogicalFileName = $file.LogicalName
            $relocate.PhysicalFileName = if ($file.Type -eq 'D') {
                Join-Path $dataPath "$targetDb`_$($file.LogicalName).mdf"
            } else {
                Join-Path $logPath "$targetDb`_$($file.LogicalName).ldf"
            }
            $restore.RelocateFiles.Add($relocate)
        }

        # Execute restore
        Write-EventSimulatorLog "Starting database restore to $targetDb"
        $restore.SqlRestore($server)

        # Verify database consistency
        $server.Databases[$targetDb].CheckTables('None')

        # Update configuration if target database changed
        if ($TargetDatabase -and ($TargetDatabase -ne $dbSettings.Database)) {
            $config.DatabaseSettings.Database = $TargetDatabase
            $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
        }

        Write-EventSimulatorLog "Database restore completed successfully"
        return $true
    }
    catch {
        Write-EventSimulatorLog "Restore failed: $_" -Level Error
        return $false
    }
}

function Start-TransactionLogBackup {
    [CmdletBinding()]
    param(
        [Parameter()]
        [int]$IntervalMinutes = $LOG_BACKUP_INTERVAL,
        
        [Parameter()]
        [string]$BackupPath = $DEFAULT_BACKUP_PATH
    )

    try {
        # Validate recovery model
        $dbSettings = [EventSimulator.Common.Configuration.DatabaseSettings]::new()
        $configPath = Join-Path $PSScriptRoot "..\..\appsettings.json"
        $config = Get-Content $configPath | ConvertFrom-Json
        $dbSettings.LoadConfiguration($config)

        $server = New-Object Microsoft.SqlServer.Management.Smo.Server($dbSettings.Server)
        $database = $server.Databases[$dbSettings.Database]

        if ($database.RecoveryModel -ne 'Full') {
            throw "Database must be in FULL recovery model for transaction log backups"
        }

        # Create scheduled task
        $taskName = "EventSimulator_LogBackup"
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Command `"Backup-EventSimulatorDatabase -BackupType Log -BackupPath '$BackupPath'`""
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
        $principal = New-ScheduledTaskPrincipal -UserID "NT SERVICE\MSSQLSERVER" -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd

        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force

        Write-EventSimulatorLog "Transaction log backup job scheduled successfully"
        return $true
    }
    catch {
        Write-EventSimulatorLog "Failed to schedule transaction log backups: $_" -Level Error
        return $false
    }
}

# Export functions
Export-ModuleMember -Function @(
    'Backup-EventSimulatorDatabase',
    'Restore-EventSimulatorDatabase',
    'Start-TransactionLogBackup'
)
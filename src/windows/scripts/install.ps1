#Requires -Version 7.2
#Requires -RunAsAdministrator
#Requires -Modules @{ModuleName='SqlServer';ModuleVersion='21.1.18256'}, @{ModuleName='Microsoft.PowerShell.Security';ModuleVersion='7.2.0'}

<#
.SYNOPSIS
    Installation script for Windows Event Simulator with comprehensive security and configuration capabilities.
.DESCRIPTION
    Enterprise-grade installation script that handles deployment, security configuration, and database setup
    for the Windows Event Simulator application across different environments.
.NOTES
    Version: 1.0.0
    Author: Windows Event Simulator Team
    Copyright: © 2024 Windows Event Simulator Project
#>

# Script Parameters
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Development', 'Testing', 'Production')]
    [string]$Environment = 'Production',

    [Parameter()]
    [string]$InstallPath = "$env:ProgramFiles\WindowsEventSimulator",

    [Parameter()]
    [string]$DatabaseServer = "localhost\SQLEXPRESS",

    [Parameter()]
    [string]$DatabaseName = "EventSimulatorDB",

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$EnableTDE,

    [Parameter()]
    [switch]$SkipDotNetCheck
)

# Global Variables
$script:LogPath = "$env:ProgramData\WindowsEventSimulator\Logs"
$script:ConfigPath = "$env:ProgramData\WindowsEventSimulator\Config"
$script:CertStorePath = "Cert:\LocalMachine\My"
$script:AuditLogPath = "$env:ProgramData\WindowsEventSimulator\Audit"
$script:BackupPath = "$env:ProgramData\WindowsEventSimulator\Backup"

# Import required modules with specific versions
$requiredModules = @{
    'SqlServer' = '21.1.18256'
    'Microsoft.PowerShell.Security' = '7.2.0'
    'ActiveDirectory' = '1.0.0'
}

function Import-RequiredModules {
    foreach ($module in $requiredModules.GetEnumerator()) {
        try {
            Import-Module -Name $module.Key -MinimumVersion $module.Value -ErrorAction Stop
        }
        catch {
            throw "Required module $($module.Key) version $($module.Value) not found. Please install it first."
        }
    }
}

function Test-Prerequisites {
    [CmdletBinding()]
    param(
        [bool]$SkipDotNetCheck,
        [string]$Environment,
        [bool]$Verbose
    )

    $status = @{
        WindowsVersion = $false
        DotNetRuntime = $false
        SqlServer = $false
        Permissions = $false
        DiskSpace = $false
        Network = $false
        Certificates = $false
        ActiveDirectory = $false
        AntiVirus = $false
        PowerShell = $false
    }

    # Verify Windows version
    $osInfo = Get-CimInstance Win32_OperatingSystem
    $status.WindowsVersion = [version]$osInfo.Version -ge [version]"10.0.17763"

    # Check .NET Runtime if not skipped
    if (-not $SkipDotNetCheck) {
        $dotnetVersion = dotnet --list-runtimes | Select-String "Microsoft.NETCore.App 6.0"
        $status.DotNetRuntime = $null -ne $dotnetVersion
    }

    # Verify SQL Server
    try {
        $sqlInfo = Invoke-SqlCmd -ServerInstance $DatabaseServer -Query "SELECT @@VERSION" -ErrorAction Stop
        $status.SqlServer = $true
    }
    catch {
        Write-Warning "SQL Server verification failed: $_"
    }

    # Check administrative permissions
    $status.Permissions = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    # Verify disk space
    $systemDrive = Get-PSDrive -Name C
    $status.DiskSpace = $systemDrive.Free -gt 10GB

    # Check network connectivity
    $status.Network = Test-NetConnection -ComputerName $DatabaseServer -InformationLevel Quiet

    # Verify certificates
    $status.Certificates = Test-Path -Path $CertStorePath

    # Check Active Directory if not Development
    if ($Environment -ne 'Development') {
        $status.ActiveDirectory = Get-Module -ListAvailable ActiveDirectory
    }
    else {
        $status.ActiveDirectory = $true
    }

    # Verify PowerShell execution policy
    $status.PowerShell = (Get-ExecutionPolicy) -ne 'Restricted'

    return $status
}

function Install-Application {
    [CmdletBinding()]
    param(
        [string]$InstallPath,
        [bool]$Force,
        [string]$Environment,
        [hashtable]$SecurityConfig
    )

    try {
        # Create directory structure
        $directories = @($InstallPath, $LogPath, $ConfigPath, $AuditLogPath, $BackupPath)
        foreach ($dir in $directories) {
            New-Item -Path $dir -ItemType Directory -Force:$Force -ErrorAction Stop
        }

        # Install SSL certificate
        $cert = New-SelfSignedCertificate -DnsName "WindowsEventSimulator" -CertStoreLocation $CertStorePath `
            -KeyUsage KeyEncipherment,DataEncipherment -KeySpec KeyExchange -KeyLength 4096 -HashAlgorithm SHA256

        # Copy application files
        $sourceFiles = @(
            "$PSScriptRoot\..\EventSimulator.UI\bin\Release\net6.0-windows\*"
            "$PSScriptRoot\..\EventSimulator.Core\bin\Release\net6.0-windows\*"
            "$PSScriptRoot\..\EventSimulator.Data\bin\Release\net6.0-windows\*"
        )
        foreach ($source in $sourceFiles) {
            Copy-Item -Path $source -Destination $InstallPath -Recurse -Force:$Force
        }

        # Configure service account
        $serviceAccount = New-LocalUser -Name "EventSimSvc" -Description "Windows Event Simulator Service Account" `
            -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires

        # Register event sources
        New-EventLog -LogName Application -Source "WindowsEventSimulator"

        # Create system restore point
        Checkpoint-Computer -Description "Windows Event Simulator Installation" -RestorePointType "APPLICATION_INSTALL"

        return @{
            Status = "Success"
            CertificateThumbprint = $cert.Thumbprint
            ServiceAccount = $serviceAccount.Name
        }
    }
    catch {
        Write-Error "Installation failed: $_"
        throw
    }
}

function Initialize-Database {
    [CmdletBinding()]
    param(
        [string]$ServerInstance,
        [string]$DatabaseName,
        [hashtable]$SecurityConfig,
        [bool]$EnableTDE
    )

    try {
        # Create database
        $query = "IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '$DatabaseName')
                  CREATE DATABASE [$DatabaseName]
                  COLLATE SQL_Latin1_General_CP1_CI_AS"
        Invoke-Sqlcmd -ServerInstance $ServerInstance -Query $query -ErrorAction Stop

        # Enable TDE if specified
        if ($EnableTDE) {
            $tdeCert = New-SelfSignedCertificate -DnsName "EventSimulatorTDE" -CertStoreLocation $CertStorePath `
                -KeyUsage KeyEncipherment -KeySpec KeyExchange -KeyLength 4096 -HashAlgorithm SHA256

            $tdeQuery = @"
                USE master;
                CREATE CERTIFICATE TDECert FROM FILE = '$($tdeCert.PSPath)';
                USE [$DatabaseName];
                CREATE DATABASE ENCRYPTION KEY
                WITH ALGORITHM = AES_256
                ENCRYPTION BY SERVER CERTIFICATE TDECert;
                ALTER DATABASE [$DatabaseName]
                SET ENCRYPTION ON;
"@
            Invoke-Sqlcmd -ServerInstance $ServerInstance -Query $tdeQuery
        }

        # Configure database security
        $securityQuery = @"
            USE [$DatabaseName];
            CREATE USER [EventSimUser] FOR LOGIN [EventSimSvc];
            ALTER ROLE db_datareader ADD MEMBER [EventSimUser];
            ALTER ROLE db_datawriter ADD MEMBER [EventSimUser];
            GRANT EXECUTE TO [EventSimUser];
"@
        Invoke-Sqlcmd -ServerInstance $ServerInstance -Query $securityQuery

        # Setup database maintenance
        $maintenanceQuery = @"
            USE [$DatabaseName];
            EXEC sp_addextendedproc 'xp_maintenance', 'xp_maintenance.dll';
            EXEC sp_configure 'show advanced options', 1;
            RECONFIGURE;
            EXEC sp_configure 'Database Mail XPs', 1;
            RECONFIGURE;
"@
        Invoke-Sqlcmd -ServerInstance $ServerInstance -Query $maintenanceQuery

        return @{
            Status = "Success"
            DatabaseName = $DatabaseName
            TDEEnabled = $EnableTDE
        }
    }
    catch {
        Write-Error "Database initialization failed: $_"
        throw
    }
}

function Set-SecurityPermissions {
    [CmdletBinding()]
    param(
        [string]$InstallPath,
        [hashtable]$SecurityConfig,
        [bool]$EnableAudit
    )

    try {
        # Configure NTFS permissions
        $acl = Get-Acl -Path $InstallPath
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            "EventSimSvc",
            "ReadAndExecute",
            "ContainerInherit,ObjectInherit",
            "None",
            "Allow"
        )
        $acl.AddAccessRule($rule)
        Set-Acl -Path $InstallPath -AclObject $acl

        # Setup audit policies
        if ($EnableAudit) {
            auditpol /set /category:"System","Security" /success:enable /failure:enable
        }

        # Configure network security
        New-NetFirewallRule -DisplayName "Windows Event Simulator" -Direction Inbound `
            -Program "$InstallPath\EventSimulator.UI.exe" -Action Allow

        # Setup encryption keys
        $keyFile = "$ConfigPath\encryption.key"
        $key = New-Object Byte[] 32
        [Security.Cryptography.RNGCryptoServiceProvider]::Create().GetBytes($key)
        $key | Set-Content -Path $keyFile -Encoding Byte

        return @{
            Status = "Success"
            AuditEnabled = $EnableAudit
            FirewallRuleCreated = $true
        }
    }
    catch {
        Write-Error "Security configuration failed: $_"
        throw
    }
}

# Main installation function
function Install-EventSimulator {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet('Development', 'Testing', 'Production')]
        [string]$Environment,

        [Parameter()]
        [string]$InstallPath = $script:InstallPath,

        [Parameter()]
        [string]$DatabaseServer = $script:DatabaseServer,

        [Parameter()]
        [switch]$Force,

        [Parameter()]
        [switch]$EnableTDE
    )

    try {
        # Import required modules
        Import-RequiredModules

        # Check prerequisites
        $prereqStatus = Test-Prerequisites -SkipDotNetCheck:$SkipDotNetCheck -Environment $Environment -Verbose:$VerbosePreference
        if (-not ($prereqStatus.Values -notcontains $false)) {
            throw "Prerequisites check failed. Please review the requirements."
        }

        # Install application
        $installResult = Install-Application -InstallPath $InstallPath -Force:$Force -Environment $Environment -SecurityConfig @{}

        # Initialize database
        $dbResult = Initialize-Database -ServerInstance $DatabaseServer -DatabaseName $DatabaseName -SecurityConfig @{} -EnableTDE:$EnableTDE

        # Configure security
        $securityResult = Set-SecurityPermissions -InstallPath $InstallPath -SecurityConfig @{} -EnableAudit:($Environment -eq 'Production')

        return @{
            Status = "Success"
            Environment = $Environment
            InstallPath = $InstallPath
            DatabaseName = $dbResult.DatabaseName
            CertificateThumbprint = $installResult.CertificateThumbprint
            ServiceAccount = $installResult.ServiceAccount
        }
    }
    catch {
        Write-Error "Installation failed: $_"
        throw
    }
}

Export-ModuleMember -Function @(
    'Install-EventSimulator',
    'Test-Prerequisites',
    'Install-Application',
    'Initialize-Database',
    'Set-SecurityPermissions'
)
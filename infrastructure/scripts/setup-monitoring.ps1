#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Sets up comprehensive monitoring infrastructure for the Windows Event Simulator.
.DESCRIPTION
    Configures ETW providers, Application Insights, performance counters, and health checks
    with support for custom monitoring endpoints and automated validation.
.NOTES
    Version: 1.0.0
    Author: Windows Event Simulator Team
#>

# Az.Monitor module v4.4.0
# Microsoft.ApplicationInsights.PerfCounterCollector v2.21.0
# Microsoft.Diagnostics.Tracing.TraceEvent v3.1.3

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'

# Global Configuration
$script:ETW_PROVIDER_NAME = 'EventSimulator-Monitoring'
$script:PERFORMANCE_LOG_PATH = 'C:\PerfLogs\EventSimulator'
$script:APP_INSIGHTS_KEY = $env:APP_INSIGHTS_KEY
$script:RETRY_COUNT = 3
$script:RETRY_DELAY_SECONDS = 5
$script:MONITORING_VERSION = '1.0.0'

# Import required modules with validation
function Install-MonitoringPrerequisites {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [hashtable]$Prerequisites = @{
            'Az.Monitor' = 'latest'
            'Microsoft.ApplicationInsights.PerfCounterCollector' = '2.21.0'
            'Microsoft.Diagnostics.Tracing.TraceEvent' = '3.1.3'
        },
        [switch]$Force
    )

    $results = @{}

    foreach ($module in $Prerequisites.GetEnumerator()) {
        try {
            Write-Verbose "Installing/updating module: $($module.Key) version $($module.Value)"
            
            if ($Force -or -not (Get-Module -ListAvailable -Name $module.Key)) {
                $installParams = @{
                    Name = $module.Key
                    Force = $Force
                    ErrorAction = 'Stop'
                    Verbose = $VerbosePreference -eq 'Continue'
                }
                
                if ($module.Value -ne 'latest') {
                    $installParams.RequiredVersion = $module.Value
                }

                Install-Module @installParams
                Import-Module -Name $module.Key -Force
                $results[$module.Key] = @{ Status = 'Success'; Version = (Get-Module $module.Key).Version }
            }
            else {
                Write-Verbose "Module $($module.Key) is already installed"
                $results[$module.Key] = @{ Status = 'Skipped'; Version = (Get-Module $module.Key).Version }
            }
        }
        catch {
            $results[$module.Key] = @{ Status = 'Failed'; Error = $_.Exception.Message }
            Write-Error "Failed to install module $($module.Key): $_"
        }
    }

    return $results
}

# Configure ETW Provider with enhanced logging
function Register-ETWProvider {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ProviderName,
        
        [Parameter(Mandatory = $true)]
        [string]$LogFilePath,
        
        [Parameter(Mandatory = $false)]
        [hashtable]$ChannelConfig = @{
            'Operational' = @{ Level = 4; Keywords = 0xFFFFFFFFFFFFFFFF }
            'Debug' = @{ Level = 5; Keywords = 0xFFFFFFFFFFFFFFFF }
            'Analytic' = @{ Level = 5; Keywords = 0xFFFFFFFFFFFFFFFF }
        }
    )

    try {
        # Create provider manifest
        $manifestPath = Join-Path $env:TEMP "$ProviderName.man"
        $providerGuid = [System.Guid]::NewGuid()

        $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<instrumentationManifest xmlns="http://schemas.microsoft.com/win/2004/08/events">
    <instrumentation xmlns:win="http://manifests.microsoft.com/win/2004/08/windows/events">
        <events xmlns="http://schemas.microsoft.com/win/2004/08/events">
            <provider name="$ProviderName" guid="{$providerGuid}" symbol="$ProviderName">
                <channels>
"@

        foreach ($channel in $ChannelConfig.GetEnumerator()) {
            $manifest += @"
                    <channel name="$($channel.Key)" type="Operational" />
"@
        }

        $manifest += @"
                </channels>
            </provider>
        </events>
    </instrumentation>
</instrumentationManifest>
"@

        $manifest | Out-File -FilePath $manifestPath -Encoding UTF8 -Force

        # Register provider
        $wevtutil = Join-Path $env:SystemRoot "System32\wevtutil.exe"
        & $wevtutil im $manifestPath

        # Configure logging
        foreach ($channel in $ChannelConfig.GetEnumerator()) {
            $channelPath = "$ProviderName/$($channel.Key)"
            & $wevtutil sl $channelPath /e:true /rt:$($channel.Value.Level) /k:$($channel.Value.Keywords)
        }

        return @{
            ProviderGuid = $providerGuid
            Channels = $ChannelConfig.Keys
            Status = 'Success'
        }
    }
    catch {
        Write-Error "Failed to register ETW provider: $_"
        throw
    }
    finally {
        if (Test-Path $manifestPath) {
            Remove-Item $manifestPath -Force
        }
    }
}

# Configure Performance Counters
function Set-PerformanceCounters {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$CategoryName,
        
        [Parameter(Mandatory = $true)]
        [string[]]$CounterNames,
        
        [Parameter(Mandatory = $false)]
        [hashtable]$CollectionConfig = @{
            SampleInterval = 15
            MaxBufferSize = 1024
            LogRotationDays = 30
        }
    )

    try {
        # Create counter category if it doesn't exist
        if (-not [System.Diagnostics.PerformanceCounterCategory]::Exists($CategoryName)) {
            $counters = @()
            foreach ($counter in $CounterNames) {
                $counters += New-Object System.Diagnostics.CounterCreationData($counter, "", PerformanceCounterType::NumberOfItems64)
            }
            
            [System.Diagnostics.PerformanceCounterCategory]::Create(
                $CategoryName,
                "Windows Event Simulator Performance Counters",
                [System.Diagnostics.PerformanceCounterCategoryType]::MultiInstance,
                $counters
            )
        }

        # Configure data collector set
        $logman = Join-Path $env:SystemRoot "System32\logman.exe"
        $collectorName = "EventSimulator_Perf_Counters"
        
        # Remove existing collector if present
        & $logman delete $collectorName -ea SilentlyContinue

        # Create new collector
        $counterPaths = $CounterNames | ForEach-Object { "\$CategoryName($_)" }
        & $logman create counter $collectorName -f bincirc -si $CollectionConfig.SampleInterval `
            -max $CollectionConfig.MaxBufferSize -o "$PERFORMANCE_LOG_PATH\perfcounter_%computername%"
        & $logman add counter $collectorName $counterPaths
        
        # Start collection
        & $logman start $collectorName

        return @{
            CategoryName = $CategoryName
            Counters = $CounterNames
            CollectorName = $collectorName
            Status = 'Success'
        }
    }
    catch {
        Write-Error "Failed to configure performance counters: $_"
        throw
    }
}

# Configure Application Insights
function Set-ApplicationInsights {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$InstrumentationKey,
        
        [Parameter(Mandatory = $false)]
        [hashtable]$CustomProperties = @{},
        
        [Parameter(Mandatory = $false)]
        [hashtable]$MetricsConfig = @{
            EnableAdaptiveSampling = $true
            SamplingRate = 100
            EnableLiveMetrics = $true
            EnableQuickPulse = $true
        }
    )

    try {
        # Validate instrumentation key
        if (-not [Guid]::TryParse($InstrumentationKey, [ref][Guid]::Empty)) {
            throw "Invalid Application Insights instrumentation key format"
        }

        # Configure Application Insights SDK
        $config = New-Object Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration
        $config.InstrumentationKey = $InstrumentationKey
        
        # Configure custom properties
        foreach ($prop in $CustomProperties.GetEnumerator()) {
            $config.TelemetryInitializers.Add(
                [Microsoft.ApplicationInsights.Extensibility.OperationCorrelationTelemetryInitializer]::new()
            )
        }

        # Configure metrics collection
        if ($MetricsConfig.EnableAdaptiveSampling) {
            $sampler = New-Object Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel.AdaptiveSamplingTelemetryProcessor
            $sampler.SamplingPercentage = $MetricsConfig.SamplingRate
            $config.TelemetryProcessors.Add($sampler)
        }

        # Enable live metrics
        if ($MetricsConfig.EnableLiveMetrics) {
            $config.TelemetryChannel.EndpointAddress = "https://rt.services.visualstudio.com/QuickPulseService.svc"
        }

        return @{
            InstrumentationKey = $InstrumentationKey
            CustomProperties = $CustomProperties.Count
            MetricsConfig = $MetricsConfig
            Status = 'Success'
        }
    }
    catch {
        Write-Error "Failed to configure Application Insights: $_"
        throw
    }
}

# Configure Health Checks
function Set-HealthChecks {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string[]]$EndpointUrls,
        
        [Parameter(Mandatory = $false)]
        [int]$CheckIntervalSeconds = 60,
        
        [Parameter(Mandatory = $false)]
        [hashtable]$AlertConfig = @{
            ResponseTimeThreshold = 5000
            ErrorThreshold = 3
            NotificationEmails = @()
        }
    )

    try {
        # Create health check tasks
        $healthChecks = @()
        
        foreach ($url in $EndpointUrls) {
            $task = @{
                Url = $url
                LastCheck = $null
                FailureCount = 0
                Status = 'Unknown'
            }
            
            # Validate endpoint
            $request = [System.Net.WebRequest]::Create($url)
            $request.Method = "HEAD"
            $request.Timeout = 5000
            
            try {
                $response = $request.GetResponse()
                $task.Status = 'Healthy'
                $response.Dispose()
            }
            catch {
                $task.Status = 'Unhealthy'
                $task.FailureCount++
            }
            
            $healthChecks += $task
        }

        # Register scheduled task for health monitoring
        $action = New-ScheduledTaskAction -Execute 'PowerShell.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\monitor-health.ps1`""
        
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
            -RepetitionInterval (New-TimeSpan -Seconds $CheckIntervalSeconds)
        
        $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount
        
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -RunOnlyIfNetworkAvailable
        
        Register-ScheduledTask -TaskName "EventSimulator_HealthCheck" -Action $action `
            -Trigger $trigger -Principal $principal -Settings $settings -Force

        return @{
            Endpoints = $EndpointUrls.Count
            CheckInterval = $CheckIntervalSeconds
            AlertConfig = $AlertConfig
            Status = 'Success'
        }
    }
    catch {
        Write-Error "Failed to configure health checks: $_"
        throw
    }
}

# Test monitoring setup
function Test-MonitoringSetup {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [hashtable]$TestConfig = @{
            ValidateETW = $true
            ValidatePerformanceCounters = $true
            ValidateApplicationInsights = $true
            ValidateHealthChecks = $true
        },
        [switch]$DetailedOutput
    )

    $results = @{}

    try {
        # Test ETW Provider
        if ($TestConfig.ValidateETW) {
            Write-Verbose "Testing ETW Provider configuration..."
            $etwTest = & $env:SystemRoot\System32\wevtutil.exe gl $ETW_PROVIDER_NAME
            $results.ETW = @{
                Status = if ($LASTEXITCODE -eq 0) { 'Success' } else { 'Failed' }
                Details = $etwTest
            }
        }

        # Test Performance Counters
        if ($TestConfig.ValidatePerformanceCounters) {
            Write-Verbose "Testing Performance Counter configuration..."
            $counterTest = Get-Counter -ListSet "Windows Event Simulator" -ErrorAction SilentlyContinue
            $results.PerformanceCounters = @{
                Status = if ($counterTest) { 'Success' } else { 'Failed' }
                CounterCount = $counterTest.Counter.Count
            }
        }

        # Test Application Insights
        if ($TestConfig.ValidateApplicationInsights -and $APP_INSIGHTS_KEY) {
            Write-Verbose "Testing Application Insights configuration..."
            $aiTest = Test-NetConnection -ComputerName "dc.services.visualstudio.com" -Port 443
            $results.ApplicationInsights = @{
                Status = if ($aiTest.TcpTestSucceeded) { 'Success' } else { 'Failed' }
                Connectivity = $aiTest.TcpTestSucceeded
            }
        }

        # Test Health Checks
        if ($TestConfig.ValidateHealthChecks) {
            Write-Verbose "Testing Health Check configuration..."
            $healthTask = Get-ScheduledTask -TaskName "EventSimulator_HealthCheck" -ErrorAction SilentlyContinue
            $results.HealthChecks = @{
                Status = if ($healthTask) { 'Success' } else { 'Failed' }
                LastResult = $healthTask.LastTaskResult
            }
        }

        if ($DetailedOutput) {
            return $results
        }
        else {
            return @{
                Status = if ($results.Values.Status -contains 'Failed') { 'Failed' } else { 'Success' }
                Components = $results.Keys.Count
            }
        }
    }
    catch {
        Write-Error "Monitoring setup validation failed: $_"
        throw
    }
}

# Main execution block
try {
    Write-Verbose "Starting monitoring infrastructure setup..."
    
    # Install prerequisites
    $prereqStatus = Install-MonitoringPrerequisites
    
    # Configure ETW Provider
    $etwStatus = Register-ETWProvider -ProviderName $ETW_PROVIDER_NAME -LogFilePath $PERFORMANCE_LOG_PATH
    
    # Configure Performance Counters
    $counterStatus = Set-PerformanceCounters -CategoryName "Windows Event Simulator" -CounterNames @(
        "Events Generated/sec",
        "Template Cache Hit Ratio",
        "Active Scenarios",
        "Generation Queue Length"
    )
    
    # Configure Application Insights if key is provided
    if ($APP_INSIGHTS_KEY) {
        $aiStatus = Set-ApplicationInsights -InstrumentationKey $APP_INSIGHTS_KEY
    }
    
    # Configure Health Checks
    $healthStatus = Set-HealthChecks -EndpointUrls @(
        "http://localhost:5000/health",
        "http://localhost:5000/metrics"
    )
    
    # Test the setup
    $testResults = Test-MonitoringSetup -DetailedOutput
    
    Write-Verbose "Monitoring infrastructure setup completed successfully"
    
    return @{
        Version = $MONITORING_VERSION
        Prerequisites = $prereqStatus
        ETWProvider = $etwStatus
        PerformanceCounters = $counterStatus
        ApplicationInsights = $aiStatus
        HealthChecks = $healthStatus
        ValidationResults = $testResults
    }
}
catch {
    Write-Error "Failed to set up monitoring infrastructure: $_"
    throw
}
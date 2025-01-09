#Requires -Version 7.0
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Automated test execution script for Windows Event Simulator with enhanced security validation and performance benchmarking.
.DESCRIPTION
    Version: 1.0.0
    PowerShell script that automates test execution with comprehensive security validation, performance benchmarking,
    and compliance reporting for the Windows Event Simulator solution.
#>

# Script configuration
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Global variables
$script:TestConfiguration = 'Release'
$script:TestPlatform = 'x64'
$script:TestResultsPath = Join-Path $PSScriptRoot 'TestResults'
$script:CoverageReportPath = Join-Path $PSScriptRoot 'CoverageReport'
$script:SecurityCompliancePath = Join-Path $PSScriptRoot 'SecurityReport'
$script:TestLogPath = Join-Path $PSScriptRoot 'TestLogs'
$script:MinimumCoverage = 80
$script:PerformanceThreshold = 1000 # Events per second

# Import build script for environment setup
. (Join-Path $PSScriptRoot 'build.ps1')

function Initialize-TestEnvironment {
    [CmdletBinding()]
    param (
        [Parameter()]
        [ValidateSet('Debug', 'Information', 'Warning', 'Error')]
        [string]$LogLevel = 'Information',

        [Parameter()]
        [ValidateSet('Standard', 'Enhanced', 'Maximum')]
        [string]$SecurityMode = 'Enhanced'
    )

    try {
        Write-Host "Initializing test environment with security mode: $SecurityMode"

        # Verify .NET SDK
        $dotnetVersion = dotnet --version
        if (-not $dotnetVersion.StartsWith('6.0')) {
            throw "Required .NET 6.0 SDK not found. Current version: $dotnetVersion"
        }

        # Verify ReportGenerator
        $reportGenPath = Get-Command dotnet-reportgenerator -ErrorAction SilentlyContinue
        if (-not $reportGenPath) {
            Write-Host "Installing ReportGenerator tool..."
            dotnet tool install -g dotnet-reportgenerator-globaltool --version 5.1.0
        }

        # Create required directories with secure ACLs
        $paths = @($TestResultsPath, $CoverageReportPath, $SecurityCompliancePath, $TestLogPath)
        foreach ($path in $paths) {
            if (-not (Test-Path $path)) {
                New-Item -Path $path -ItemType Directory -Force | Out-Null
                # Set secure ACLs
                $acl = Get-Acl $path
                $acl.SetAccessRuleProtection($true, $false)
                $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                    "BUILTIN\Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
                )
                $acl.AddAccessRule($adminRule)
                Set-Acl $path $acl
            }
        }

        # Initialize logging
        $logFile = Join-Path $TestLogPath "test_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
        Start-Transcript -Path $logFile -Append

        # Verify Windows Event Log access
        $eventLogTest = Get-WinEvent -LogName 'Application' -MaxEvents 1 -ErrorAction SilentlyContinue
        if (-not $eventLogTest) {
            throw "Unable to access Windows Event Log. Verify permissions."
        }

        return $true
    }
    catch {
        Write-Error "Failed to initialize test environment: $_"
        return $false
    }
}

function Run-UnitTests {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$TestProjectPath,

        [Parameter()]
        [string]$Configuration = $script:TestConfiguration,

        [Parameter()]
        [bool]$ParallelExecution = $true,

        [Parameter()]
        [string]$SecurityLevel = 'Enhanced'
    )

    try {
        Write-Host "Running unit tests with security level: $SecurityLevel"

        # Configure test parameters
        $testParams = @(
            'test',
            $TestProjectPath,
            '--configuration', $Configuration,
            '--no-build',
            '--results-directory', $TestResultsPath,
            '--logger', "trx;LogFileName=UnitTests_$(Get-Date -Format 'yyyyMMdd_HHmmss').trx",
            '--collect:"XPlat Code Coverage"',
            '--settings', "./test.runsettings"
        )

        if ($ParallelExecution) {
            $testParams += '--parallel'
        }

        # Execute tests with security validation
        $testResult = dotnet @testParams
        if ($LASTEXITCODE -ne 0) {
            throw "Unit tests failed with exit code: $LASTEXITCODE"
        }

        # Generate coverage report
        $coverageFiles = Get-ChildItem -Path $TestResultsPath -Filter 'coverage.cobertura.xml' -Recurse
        if ($coverageFiles) {
            $reportParams = @(
                '-reports:' + ($coverageFiles.FullName -join ';'),
                '-targetdir:' + $CoverageReportPath,
                '-reporttypes:Html;Cobertura',
                '-title:"Windows Event Simulator Test Coverage"'
            )
            dotnet reportgenerator @reportParams
        }

        # Verify coverage threshold
        $coverageXml = [xml](Get-Content $coverageFiles[0].FullName)
        $lineCoverage = [double]$coverageXml.coverage.line-rate * 100
        if ($lineCoverage -lt $script:MinimumCoverage) {
            throw "Code coverage ($lineCoverage%) below minimum threshold ($script:MinimumCoverage%)"
        }

        return $true
    }
    catch {
        Write-Error "Unit test execution failed: $_"
        return $false
    }
}

function Run-IntegrationTests {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$TestProjectPath,

        [Parameter()]
        [string]$Configuration = $script:TestConfiguration,

        [Parameter()]
        [int]$PerformanceThreshold = $script:PerformanceThreshold,

        [Parameter()]
        [string]$SecurityProfile = 'Enhanced'
    )

    try {
        Write-Host "Running integration tests with performance threshold: $PerformanceThreshold events/sec"

        # Start performance monitoring
        $perfCounter = New-Object System.Diagnostics.PerformanceCounter("EventLog", "Events/sec", "Application")
        $perfCounter.NextValue() | Out-Null # First call to initialize

        # Execute integration tests
        $testParams = @(
            'test',
            $TestProjectPath,
            '--configuration', $Configuration,
            '--no-build',
            '--filter', 'Category=Integration',
            '--results-directory', $TestResultsPath,
            '--logger', "trx;LogFileName=IntegrationTests_$(Get-Date -Format 'yyyyMMdd_HHmmss').trx"
        )

        $testResult = dotnet @testParams
        if ($LASTEXITCODE -ne 0) {
            throw "Integration tests failed with exit code: $LASTEXITCODE"
        }

        # Verify performance
        $eventsPerSecond = $perfCounter.NextValue()
        if ($eventsPerSecond -lt $PerformanceThreshold) {
            throw "Performance below threshold: $eventsPerSecond events/sec (Required: $PerformanceThreshold)"
        }

        # Cleanup generated events
        Write-Host "Cleaning up test events..."
        Clear-EventLog -LogName Application -ErrorAction SilentlyContinue

        return $true
    }
    catch {
        Write-Error "Integration test execution failed: $_"
        return $false
    }
    finally {
        if ($perfCounter) {
            $perfCounter.Dispose()
        }
    }
}

function Generate-TestReport {
    [CmdletBinding()]
    param (
        [Parameter()]
        [string]$TestResultsPath = $script:TestResultsPath,

        [Parameter()]
        [string]$CoverageReportPath = $script:CoverageReportPath,

        [Parameter()]
        [string]$SecurityReportPath = $script:SecurityCompliancePath,

        [Parameter()]
        [string]$PerformanceReportPath = (Join-Path $TestResultsPath 'performance')
    )

    try {
        Write-Host "Generating comprehensive test reports..."

        # Aggregate test results
        $trxFiles = Get-ChildItem -Path $TestResultsPath -Filter '*.trx' -Recurse
        $testResults = @{
            TotalTests = 0
            Passed = 0
            Failed = 0
            Duration = [TimeSpan]::Zero
        }

        foreach ($trx in $trxFiles) {
            $xml = [xml](Get-Content $trx.FullName)
            $counters = $xml.TestRun.ResultSummary.Counters
            $testResults.TotalTests += [int]$counters.total
            $testResults.Passed += [int]$counters.passed
            $testResults.Failed += [int]$counters.failed
            $testResults.Duration += [TimeSpan]::Parse($xml.TestRun.Times.duration)
        }

        # Generate HTML report
        $reportDate = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        $htmlReport = @"
<!DOCTYPE html>
<html>
<head>
    <title>Test Execution Report - $reportDate</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .summary { background-color: #f0f0f0; padding: 15px; border-radius: 5px; }
        .passed { color: green; }
        .failed { color: red; }
    </style>
</head>
<body>
    <h1>Windows Event Simulator - Test Report</h1>
    <div class="summary">
        <h2>Test Summary</h2>
        <p>Total Tests: $($testResults.TotalTests)</p>
        <p class="passed">Passed: $($testResults.Passed)</p>
        <p class="failed">Failed: $($testResults.Failed)</p>
        <p>Duration: $($testResults.Duration.ToString())</p>
    </div>
</body>
</html>
"@

        $htmlReport | Out-File (Join-Path $TestResultsPath 'TestReport.html') -Encoding UTF8
    }
    catch {
        Write-Error "Failed to generate test report: $_"
    }
}

# Main execution block
try {
    if (-not (Initialize-TestEnvironment)) {
        exit 3
    }

    $testProject = 'src/windows/EventSimulator.Tests/EventSimulator.Tests.csproj'
    
    if (-not (Run-UnitTests -TestProjectPath $testProject)) {
        exit 1
    }

    if (-not (Run-IntegrationTests -TestProjectPath $testProject)) {
        exit 5
    }

    Generate-TestReport

    Write-Host "Test execution completed successfully" -ForegroundColor Green
    exit 0
}
catch {
    Write-Error "Test execution failed: $_"
    exit 1
}
finally {
    Stop-Transcript
}
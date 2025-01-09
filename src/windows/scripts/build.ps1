<#
.SYNOPSIS
    Build script for Windows Event Simulator solution.
.DESCRIPTION
    Automates the build process with environment validation, package restoration,
    compilation, testing and reporting capabilities.
.NOTES
    Version: 1.0.0
    Requires: 
    - PowerShell 5.1+
    - Visual Studio 2022 (17.0.0+)
    - .NET SDK 6.0.0+
#>

#Requires -Version 5.1
#Requires -RunAsAdministrator

# Script constants
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Build configuration
$script:config = @{
    SolutionPath = Join-Path $PSScriptRoot ".." "EventSimulator.sln"
    Configuration = "Release"
    Platform = "x64"
    BuildOutput = Join-Path $PSScriptRoot ".." "artifacts"
    TestCoverageThreshold = 80
    MaxParallelJobs = 0 # Auto-detect CPU count
    VSMinVersion = "17.0.0"
    LogFile = Join-Path $PSScriptRoot "build.log"
    ErrorLog = Join-Path $PSScriptRoot "error.log"
    BuildReport = Join-Path $PSScriptRoot "buildreport.xml"
}

# Initialize logging
function Initialize-Logging {
    if (Test-Path $script:config.LogFile) {
        Remove-Item $script:config.LogFile -Force
    }
    if (Test-Path $script:config.ErrorLog) {
        Remove-Item $script:config.ErrorLog -Force
    }
    
    Start-Transcript -Path $script:config.LogFile -Append
}

function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        [ValidateSet('Information','Warning','Error')]
        [string]$Level = 'Information'
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    switch ($Level) {
        'Information' { Write-Host $logMessage -ForegroundColor Green }
        'Warning' { Write-Host $logMessage -ForegroundColor Yellow }
        'Error' { 
            Write-Host $logMessage -ForegroundColor Red
            Add-Content -Path $script:config.ErrorLog -Value $logMessage
        }
    }
    Add-Content -Path $script:config.LogFile -Value $logMessage
}

function Initialize-BuildEnvironment {
    param(
        [switch]$CleanBuild,
        [string]$VSVersion = $script:config.VSMinVersion
    )
    
    try {
        Write-Log "Initializing build environment..."
        
        # Verify Visual Studio installation
        $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (-not (Test-Path $vsWhere)) {
            throw "Visual Studio 2022 installation not found"
        }
        
        $vsPath = & $vsWhere -version $VSVersion -property installationPath
        if (-not $vsPath) {
            throw "Visual Studio 2022 version $VSVersion or higher is required"
        }
        
        # Verify .NET SDK
        $dotnetVersion = & dotnet --version
        if (-not $dotnetVersion.StartsWith("6.")) {
            throw ".NET SDK 6.0 is required"
        }
        
        # Create/clean build output directory
        if ($CleanBuild -and (Test-Path $script:config.BuildOutput)) {
            Remove-Item $script:config.BuildOutput -Recurse -Force
        }
        New-Item -ItemType Directory -Path $script:config.BuildOutput -Force | Out-Null
        
        # Configure parallel processing
        if ($script:config.MaxParallelJobs -eq 0) {
            $script:config.MaxParallelJobs = [int]$env:NUMBER_OF_PROCESSORS
        }
        
        Write-Log "Build environment initialized successfully"
        return $true
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $false
    }
}

function Restore-Solution {
    param(
        [string]$SolutionPath = $script:config.SolutionPath,
        [switch]$CleanCache
    )
    
    try {
        Write-Log "Starting solution restore..."
        
        if ($CleanCache) {
            Write-Log "Cleaning NuGet cache..."
            & dotnet nuget locals all --clear
        }
        
        # Restore packages with retry logic
        $maxRetries = 3
        $retryCount = 0
        $restored = $false
        
        while (-not $restored -and $retryCount -lt $maxRetries) {
            try {
                & dotnet restore $SolutionPath --verbosity normal
                if ($LASTEXITCODE -eq 0) {
                    $restored = $true
                }
                else {
                    $retryCount++
                    Write-Log "Restore attempt $retryCount failed. Retrying..." -Level Warning
                    Start-Sleep -Seconds ($retryCount * 5)
                }
            }
            catch {
                $retryCount++
                Write-Log $_.Exception.Message -Level Warning
            }
        }
        
        if (-not $restored) {
            throw "Package restoration failed after $maxRetries attempts"
        }
        
        Write-Log "Solution restore completed successfully"
        return $true
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $false
    }
}

function Build-Solution {
    param(
        [string]$Configuration = $script:config.Configuration,
        [string]$Platform = $script:config.Platform,
        [switch]$Parallel
    )
    
    try {
        Write-Log "Starting solution build..."
        
        $buildArgs = @(
            $script:config.SolutionPath
            "/p:Configuration=$Configuration"
            "/p:Platform=$Platform"
            "/p:TreatWarningsAsErrors=true"
            "/p:Deterministic=true"
            "/p:RunCodeAnalysis=true"
            "/bl:$($script:config.BuildReport)"
            "/v:normal"
        )
        
        if ($Parallel) {
            $buildArgs += "/m:$($script:config.MaxParallelJobs)"
        }
        
        & dotnet build @buildArgs
        
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
        
        # Copy build outputs
        $artifactsDir = Join-Path $script:config.BuildOutput $Configuration
        Get-ChildItem -Path (Split-Path $script:config.SolutionPath) -Recurse -Include "bin\$Configuration\*" |
            Copy-Item -Destination $artifactsDir -Force -Recurse
        
        Write-Log "Solution build completed successfully"
        return $true
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $false
    }
}

function Run-Tests {
    param(
        [string]$TestProjectPath = (Join-Path (Split-Path $script:config.SolutionPath) "EventSimulator.Tests"),
        [int]$CoverageThreshold = $script:config.TestCoverageThreshold
    )
    
    try {
        Write-Log "Starting test execution..."
        
        $testArgs = @(
            "test"
            $TestProjectPath
            "--configuration", $script:config.Configuration
            "--no-build"
            "--verbosity", "normal"
            "/p:CollectCoverage=true"
            "/p:CoverletOutputFormat=cobertura"
            "/p:CoverletOutput=$($script:config.BuildOutput)/coverage/"
            "/p:Threshold=$CoverageThreshold"
        )
        
        & dotnet @testArgs
        
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed with exit code $LASTEXITCODE"
        }
        
        Write-Log "Test execution completed successfully"
        return $true
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $false
    }
}

# Main build script execution
try {
    Initialize-Logging
    
    Write-Log "Starting build process for Windows Event Simulator..."
    
    # Execute build steps
    $success = Initialize-BuildEnvironment -CleanBuild
    if (-not $success) { exit 3 }
    
    $success = Restore-Solution -CleanCache
    if (-not $success) { exit 4 }
    
    $success = Build-Solution -Parallel
    if (-not $success) { exit 1 }
    
    $success = Run-Tests
    if (-not $success) { exit 2 }
    
    Write-Log "Build process completed successfully"
    exit 0
}
catch {
    Write-Log $_.Exception.Message -Level Error
    exit 1
}
finally {
    Stop-Transcript
}
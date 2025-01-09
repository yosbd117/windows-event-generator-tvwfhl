<#
.SYNOPSIS
    Packaging script for Windows Event Simulator application.
.DESCRIPTION
    Creates production-ready deployment artifacts including MSI installer,
    Docker containers, and NuGet packages with comprehensive security validation.
.NOTES
    Version: 1.0.0
    Author: Windows Event Simulator Team
    Requirements:
    - PowerShell 5.1+
    - WiX Toolset v3.11.2
    - Docker CLI
    - NuGet CLI v6.0.0
    - Windows SDK 10.0 (SignTool)
    - Trivy Scanner
#>

#Requires -Version 5.1
#Requires -RunAsAdministrator

# Import build script functionality
. "$PSScriptRoot\build.ps1"

# Script constants and configuration
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Global variables
$script:PackageVersion = $env:BUILD_VERSION ?? '1.0.0'
$script:ArtifactsPath = './artifacts'
$script:InstallerPath = './artifacts/installer'
$script:DockerfilePath = '../../infrastructure/docker'
$script:CertificatePath = $env:SIGN_CERT_PATH
$script:CertificatePassword = $env:SIGN_CERT_PASSWORD
$script:DockerRegistry = $env:DOCKER_REGISTRY
$script:NuGetApiKey = $env:NUGET_API_KEY
$script:BuildConfiguration = $env:BUILD_CONFIGURATION ?? 'Release'

# Initialize packaging environment with security checks
function Initialize-PackageEnvironment {
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet('Development','Staging','Production')]
        [string]$Environment,
        [hashtable]$Configuration = @{}
    )
    
    try {
        Write-Log "Initializing packaging environment for $Environment..."
        
        # Verify required tools
        $tools = @{
            'candle.exe' = 'WiX Toolset v3.11.2'
            'docker' = 'Docker CLI'
            'nuget' = 'NuGet CLI v6.0.0'
            'signtool.exe' = 'Windows SDK 10.0'
            'trivy' = 'Trivy Scanner'
        }
        
        foreach ($tool in $tools.Keys) {
            if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
                throw "Required tool not found: $($tools[$tool])"
            }
        }
        
        # Validate code signing certificate if in Production
        if ($Environment -eq 'Production') {
            if (-not $script:CertificatePath -or -not (Test-Path $script:CertificatePath)) {
                throw "Code signing certificate not found"
            }
            if (-not $script:CertificatePassword) {
                throw "Code signing certificate password not provided"
            }
        }
        
        # Verify Docker security configuration
        $dockerInfo = docker info --format '{{json .}}'
        $dockerConfig = $dockerInfo | ConvertFrom-Json
        if (-not $dockerConfig.SecurityOptions) {
            Write-Log "Warning: Docker security options not configured" -Level Warning
        }
        
        # Create clean package directories
        @($script:ArtifactsPath, $script:InstallerPath) | ForEach-Object {
            if (Test-Path $_) {
                Remove-Item $_ -Recurse -Force
            }
            New-Item -ItemType Directory -Path $_ -Force | Out-Null
        }
        
        Write-Log "Package environment initialized successfully"
        return $true
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $false
    }
}

# Create MSI installer package
function Create-MSIPackage {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Version,
        [ValidateSet('Debug','Release')]
        [string]$Configuration = $script:BuildConfiguration,
        [hashtable]$Properties = @{}
    )
    
    try {
        Write-Log "Creating MSI package version $Version..."
        
        # Generate WiX source with security hardening
        $wixConfig = @{
            ProductName = "Windows Event Simulator"
            Manufacturer = "Security Tools Inc."
            UpgradeCode = "12345678-1234-1234-1234-123456789012"
            Platform = "x64"
            SecurityFeatures = @{
                EnableFirewall = $true
                RequireElevation = $true
                SecureProperties = $true
            }
        }
        
        # Create and sign MSI
        $msiPath = Join-Path $script:InstallerPath "EventSimulator-$Version.msi"
        
        # Build MSI with security features
        & candle.exe -arch x64 -ext WixUIExtension -ext WixUtilExtension `
            -dVersion=$Version -dConfiguration=$Configuration `
            ".\installer\Product.wxs" -out ".\installer\Product.wixobj"
            
        & light.exe -ext WixUIExtension -ext WixUtilExtension `
            -cultures:en-us -out $msiPath ".\installer\Product.wixobj"
            
        if ($LASTEXITCODE -ne 0) {
            throw "MSI creation failed"
        }
        
        # Sign MSI for production
        if ($Configuration -eq 'Release' -and $script:CertificatePath) {
            & signtool.exe sign /f $script:CertificatePath /p $script:CertificatePassword `
                /tr http://timestamp.digicert.com /td sha256 /fd sha256 $msiPath
            
            if ($LASTEXITCODE -ne 0) {
                throw "MSI signing failed"
            }
        }
        
        # Generate package manifest
        $manifest = @{
            Version = $Version
            BuildTime = Get-Date -Format "o"
            Sha256 = Get-FileHash $msiPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash
            SignatureValid = $null
        }
        
        # Verify signature if signed
        if ($Configuration -eq 'Release') {
            $signature = Get-AuthenticodeSignature $msiPath
            $manifest.SignatureValid = $signature.Status -eq 'Valid'
        }
        
        $manifest | ConvertTo-Json | Set-Content -Path "$msiPath.manifest.json"
        
        Write-Log "MSI package created successfully at $msiPath"
        return @{
            Path = $msiPath
            Manifest = $manifest
        }
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $null
    }
}

# Create Docker container images
function Create-DockerImages {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Version,
        [hashtable]$BuildArgs = @{}
    )
    
    try {
        Write-Log "Creating Docker images version $Version..."
        
        # Scan base images first
        & trivy image --no-progress --severity HIGH,CRITICAL `
            mcr.microsoft.com/windows/servercore:ltsc2022
        
        if ($LASTEXITCODE -ne 0) {
            throw "Base image security scan failed"
        }
        
        # Build images with multi-stage optimization
        $images = @(
            @{
                Name = "eventsimulator-service"
                File = "Dockerfile.service"
                Context = $script:DockerfilePath
            },
            @{
                Name = "eventsimulator-ui"
                File = "Dockerfile.ui"
                Context = $script:DockerfilePath
            }
        )
        
        $results = @()
        
        foreach ($image in $images) {
            $imageName = "$($script:DockerRegistry)/$($image.Name):$Version"
            
            # Build with security options
            docker build --no-cache --pull `
                --build-arg VERSION=$Version `
                --build-arg BUILD_CONFIGURATION=$script:BuildConfiguration `
                --security-opt no-new-privileges=true `
                -t $imageName `
                -f "$($image.Context)/$($image.File)" .
                
            if ($LASTEXITCODE -ne 0) {
                throw "Docker build failed for $($image.Name)"
            }
            
            # Generate SBOM
            & trivy image --format cyclonedx --output "$($script:ArtifactsPath)/$($image.Name)-sbom.xml" $imageName
            
            # Scan for vulnerabilities
            $scanResult = & trivy image --format json --output "$($script:ArtifactsPath)/$($image.Name)-scan.json" $imageName
            
            # Push if registry configured
            if ($script:DockerRegistry) {
                docker push $imageName
            }
            
            $results += @{
                ImageName = $imageName
                SBOM = "$($script:ArtifactsPath)/$($image.Name)-sbom.xml"
                SecurityScan = "$($script:ArtifactsPath)/$($image.Name)-scan.json"
            }
        }
        
        Write-Log "Docker images created successfully"
        return $results
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $null
    }
}

# Create NuGet packages
function Create-NuGetPackages {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Version,
        [hashtable]$Properties = @{}
    )
    
    try {
        Write-Log "Creating NuGet packages version $Version..."
        
        $packages = @(
            @{
                Project = "EventSimulator.Common"
                GenerateSymbols = $true
            },
            @{
                Project = "EventSimulator.Core"
                GenerateSymbols = $true
            }
        )
        
        $results = @()
        
        foreach ($package in $packages) {
            $projectPath = Join-Path $PSScriptRoot ".." "$($package.Project)/$($package.Project).csproj"
            
            # Create main package
            & dotnet pack $projectPath `
                --configuration $script:BuildConfiguration `
                --output $script:ArtifactsPath `
                /p:Version=$Version `
                /p:SymbolPackageFormat=snupkg `
                /p:IncludeSymbols=$($package.GenerateSymbols)
                
            if ($LASTEXITCODE -ne 0) {
                throw "Package creation failed for $($package.Project)"
            }
            
            $nupkgPath = Join-Path $script:ArtifactsPath "$($package.Project).$Version.nupkg"
            
            # Sign package if certificate available
            if ($script:CertificatePath) {
                & nuget sign $nupkgPath `
                    -CertificatePath $script:CertificatePath `
                    -CertificatePassword $script:CertificatePassword `
                    -Timestamper http://timestamp.digicert.com
                    
                if ($LASTEXITCODE -ne 0) {
                    throw "Package signing failed for $($package.Project)"
                }
            }
            
            $results += @{
                Package = $nupkgPath
                Symbols = if ($package.GenerateSymbols) {
                    Join-Path $script:ArtifactsPath "$($package.Project).$Version.snupkg"
                } else { $null }
            }
        }
        
        Write-Log "NuGet packages created successfully"
        return $results
    }
    catch {
        Write-Log $_.Exception.Message -Level Error
        return $null
    }
}

# Main execution
try {
    Write-Log "Starting packaging process for Windows Event Simulator v$script:PackageVersion..."
    
    # Initialize environment
    $success = Initialize-PackageEnvironment -Environment Production
    if (-not $success) { exit 1 }
    
    # Build solution first
    $success = Build-Solution -Configuration $script:BuildConfiguration
    if (-not $success) { exit 1 }
    
    # Create packages
    $msiResult = Create-MSIPackage -Version $script:PackageVersion
    if (-not $msiResult) { exit 2 }
    
    $dockerResult = Create-DockerImages -Version $script:PackageVersion
    if (-not $dockerResult) { exit 3 }
    
    $nugetResult = Create-NuGetPackages -Version $script:PackageVersion
    if (-not $nugetResult) { exit 4 }
    
    # Generate final manifest
    $packageManifest = @{
        Version = $script:PackageVersion
        Timestamp = Get-Date -Format "o"
        MSI = $msiResult
        Docker = $dockerResult
        NuGet = $nugetResult
    }
    
    $packageManifest | ConvertTo-Json -Depth 10 | 
        Set-Content -Path (Join-Path $script:ArtifactsPath "package-manifest.json")
    
    Write-Log "Packaging process completed successfully"
    exit 0
}
catch {
    Write-Log $_.Exception.Message -Level Error
    exit 1
}
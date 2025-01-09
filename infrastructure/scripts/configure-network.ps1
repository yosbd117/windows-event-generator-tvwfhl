#Requires -Version 7.2
#Requires -RunAsAdministrator
#Requires -Modules @{ModuleName='NetSecurity'; ModuleVersion='7.2.0'},
#                 @{ModuleName='NetTCPIP'; ModuleVersion='7.2.0'},
#                 @{ModuleName='NetAdapter'; ModuleVersion='7.2.0'},
#                 @{ModuleName='PKI'; ModuleVersion='7.2.0'}

<#
.SYNOPSIS
    Configures network infrastructure for Windows Event Simulator with comprehensive security and performance optimizations.
.DESCRIPTION
    Production-grade network configuration script that sets up Docker overlay networks, Kubernetes networking,
    and Windows Firewall rules with advanced security features and performance optimization.
.NOTES
    Version: 1.0.0
    Author: Windows Event Simulator Team
#>

# Script-level variables
$script:NETWORK_CONFIG_PATH = "$PSScriptRoot\..\..\kubernetes\config-maps.yaml"
$script:DOCKER_NETWORK_NAME = "eventsim_network"
$script:INGRESS_PORT = 443
$script:EVENT_GENERATOR_PORT = 80
$script:TEMPLATE_SERVICE_PORT = 8080
$script:MONITORING_PORT = 8081
$script:TLS_CERT_PATH = "$PSScriptRoot\..\..\certificates"
$script:NETWORK_POLICY_PATH = "$PSScriptRoot\..\..\policies"
$script:LOG_PATH = "$PSScriptRoot\..\..\logs\network.log"

# Initialize logging
function Initialize-Logging {
    if (-not (Test-Path (Split-Path $script:LOG_PATH))) {
        New-Item -ItemType Directory -Path (Split-Path $script:LOG_PATH) -Force | Out-Null
    }
    Start-Transcript -Path $script:LOG_PATH -Append
}

function Write-Log {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [ValidateSet('Info', 'Warning', 'Error')]
        [string]$Level = 'Info'
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Output $logMessage
    Add-Content -Path $script:LOG_PATH -Value $logMessage
}

function Configure-DockerNetwork {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$NetworkName,
        [Parameter(Mandatory)]
        [hashtable]$NetworkConfig,
        [switch]$EnableEncryption = $true
    )
    
    try {
        Write-Log "Starting Docker network configuration for $NetworkName"
        
        # Validate existing network
        $existingNetwork = docker network ls --filter name=$NetworkName --format '{{.Name}}'
        if ($existingNetwork) {
            Write-Log "Removing existing network $NetworkName"
            docker network rm $NetworkName
        }
        
        # Build network creation command
        $networkCmd = @(
            "docker network create"
            "--driver overlay"
            "--attachable"
            "--subnet=$($NetworkConfig.Subnet)"
            "--gateway=$($NetworkConfig.Gateway)"
            if ($EnableEncryption) {
                "--opt encrypted=true"
                "--opt com.docker.network.driver.encryption.key.rotation=12h"
            }
            "--opt com.docker.network.driver.mtu=9000"
            $NetworkName
        )
        
        # Create network
        $result = Invoke-Expression ($networkCmd -join ' ')
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create Docker network"
        }
        
        # Configure network policies
        $policyConfig = Get-Content "$script:NETWORK_POLICY_PATH\docker-policies.json" | ConvertFrom-Json
        foreach ($policy in $policyConfig.policies) {
            docker network connect $NetworkName $policy.container --alias $policy.alias
        }
        
        Write-Log "Docker network $NetworkName configured successfully"
        return [PSCustomObject]@{
            NetworkName = $NetworkName
            Status = "Configured"
            Encryption = $EnableEncryption
            MTU = 9000
            Policies = $policyConfig.policies.Count
        }
    }
    catch {
        Write-Log "Error configuring Docker network: $_" -Level Error
        throw
    }
}

function Configure-KubernetesNetwork {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$ClusterName,
        [Parameter(Mandatory)]
        [hashtable]$NetworkPolicies,
        [Parameter(Mandatory)]
        [string]$CertificatePath
    )
    
    try {
        Write-Log "Starting Kubernetes network configuration for cluster $ClusterName"
        
        # Validate cluster access
        $clusterContext = kubectl config current-context
        if ($clusterContext -ne $ClusterName) {
            throw "Not connected to correct Kubernetes cluster"
        }
        
        # Configure TLS certificates
        $certConfig = @{
            Path = $CertificatePath
            Subject = "CN=eventsim.local"
            KeyLength = 4096
            HashAlgorithm = "SHA512"
            KeyUsage = "KeyEncipherment, DigitalSignature"
            Provider = "Microsoft Enhanced RSA and AES Cryptographic Provider"
        }
        
        $cert = New-SelfSignedCertificate @certConfig
        Export-PfxCertificate -Cert $cert -FilePath "$CertificatePath\eventsim.pfx" -Password (ConvertTo-SecureString -String "EventSim2023!" -AsPlainText -Force)
        
        # Apply network policies
        foreach ($policy in $NetworkPolicies.GetEnumerator()) {
            $policyPath = "$script:NETWORK_POLICY_PATH\$($policy.Key).yaml"
            kubectl apply -f $policyPath
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to apply network policy: $($policy.Key)"
            }
        }
        
        # Configure service mesh
        kubectl apply -f "$script:NETWORK_CONFIG_PATH\service-mesh.yaml"
        
        # Setup monitoring
        kubectl apply -f "$script:NETWORK_CONFIG_PATH\network-monitoring.yaml"
        
        Write-Log "Kubernetes network configured successfully"
        return [PSCustomObject]@{
            ClusterName = $ClusterName
            Status = "Configured"
            PoliciesApplied = $NetworkPolicies.Count
            TLSEnabled = $true
            ServiceMesh = "Configured"
        }
    }
    catch {
        Write-Log "Error configuring Kubernetes network: $_" -Level Error
        throw
    }
}

function Configure-WindowsFirewall {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [hashtable]$FirewallPolicy,
        [Parameter(Mandatory)]
        [array]$AllowedPorts,
        [switch]$EnableLogging = $true
    )
    
    try {
        Write-Log "Starting Windows Firewall configuration"
        
        # Backup existing rules
        $backupPath = "$script:NETWORK_POLICY_PATH\firewall-backup.wfw"
        Export-NetFirewallRule -Path $backupPath
        
        # Configure default deny
        Set-NetFirewallProfile -Profile Domain,Private,Public -DefaultInboundAction Block -DefaultOutboundAction Block
        
        # Configure logging
        if ($EnableLogging) {
            Set-NetFirewallProfile -Profile Domain,Private,Public -LogAllowed True -LogBlocked True -LogIgnored True
        }
        
        # Configure application rules
        foreach ($rule in $FirewallPolicy.Rules) {
            $ruleParams = @{
                Name = $rule.Name
                DisplayName = $rule.DisplayName
                Direction = $rule.Direction
                Action = $rule.Action
                Protocol = $rule.Protocol
                Program = $rule.Program
                Service = $rule.Service
                Group = "EventSimulator"
                Enabled = $true
            }
            
            New-NetFirewallRule @ruleParams
        }
        
        # Configure port rules
        foreach ($port in $AllowedPorts) {
            $portParams = @{
                Name = "EventSim-Port-$port"
                DisplayName = "Event Simulator Port $port"
                Direction = "Inbound"
                Action = "Allow"
                Protocol = "TCP"
                LocalPort = $port
                Group = "EventSimulator"
                Enabled = $true
            }
            
            New-NetFirewallRule @portParams
        }
        
        Write-Log "Windows Firewall configured successfully"
        return [PSCustomObject]@{
            Status = "Configured"
            RulesApplied = $FirewallPolicy.Rules.Count + $AllowedPorts.Count
            DefaultPolicy = "Deny"
            LoggingEnabled = $EnableLogging
            BackupPath = $backupPath
        }
    }
    catch {
        Write-Log "Error configuring Windows Firewall: $_" -Level Error
        throw
    }
}

function Test-NetworkConnectivity {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [array]$Endpoints,
        [Parameter(Mandatory)]
        [hashtable]$TestConfig,
        [switch]$Detailed
    )
    
    try {
        Write-Log "Starting network connectivity tests"
        
        $results = @()
        foreach ($endpoint in $Endpoints) {
            $testResult = @{
                Endpoint = $endpoint
                TCPTest = Test-NetConnection -ComputerName $endpoint.Host -Port $endpoint.Port
                TLSVersion = $null
                Latency = $null
                DNSResolution = $null
            }
            
            # Test TLS
            try {
                $tls = Invoke-WebRequest -Uri "https://$($endpoint.Host):$($endpoint.Port)" -Method HEAD
                $testResult.TLSVersion = $tls.SecurityProtocol
            }
            catch {
                $testResult.TLSVersion = "Failed"
            }
            
            # Test latency
            $latency = Test-Connection -TargetName $endpoint.Host -Count 4 -IPv4
            $testResult.Latency = ($latency | Measure-Object ResponseTime -Average).Average
            
            # Test DNS
            $dns = Resolve-DnsName -Name $endpoint.Host -Type A -ErrorAction SilentlyContinue
            $testResult.DNSResolution = if ($dns) { "Success" } else { "Failed" }
            
            $results += [PSCustomObject]$testResult
        }
        
        Write-Log "Network connectivity tests completed"
        return [PSCustomObject]@{
            TestTime = Get-Date
            Results = $results
            DetailedOutput = if ($Detailed) { $results | ConvertTo-Json -Depth 10 } else { $null }
        }
    }
    catch {
        Write-Log "Error testing network connectivity: $_" -Level Error
        throw
    }
}

function Configure-EventSimulatorNetwork {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$ClusterName,
        [Parameter(Mandatory)]
        [hashtable]$NetworkConfig,
        [switch]$EnableEncryption = $true,
        [switch]$EnableLogging = $true
    )
    
    try {
        Initialize-Logging
        Write-Log "Starting Event Simulator network configuration"
        
        # Configure Docker network
        $dockerConfig = @{
            Subnet = $NetworkConfig.DockerSubnet
            Gateway = $NetworkConfig.DockerGateway
        }
        $dockerResult = Configure-DockerNetwork -NetworkName $script:DOCKER_NETWORK_NAME -NetworkConfig $dockerConfig -EnableEncryption:$EnableEncryption
        
        # Configure Kubernetes network
        $k8sResult = Configure-KubernetesNetwork -ClusterName $ClusterName -NetworkPolicies $NetworkConfig.KubernetesPolicies -CertificatePath $script:TLS_CERT_PATH
        
        # Configure Windows Firewall
        $firewallPorts = @($script:INGRESS_PORT, $script:EVENT_GENERATOR_PORT, $script:TEMPLATE_SERVICE_PORT, $script:MONITORING_PORT)
        $firewallResult = Configure-WindowsFirewall -FirewallPolicy $NetworkConfig.FirewallPolicy -AllowedPorts $firewallPorts -EnableLogging:$EnableLogging
        
        # Test connectivity
        $endpoints = @(
            @{ Host = "localhost"; Port = $script:INGRESS_PORT },
            @{ Host = "localhost"; Port = $script:EVENT_GENERATOR_PORT },
            @{ Host = "localhost"; Port = $script:TEMPLATE_SERVICE_PORT }
        )
        $testResult = Test-NetworkConnectivity -Endpoints $endpoints -TestConfig $NetworkConfig.TestConfig -Detailed
        
        Write-Log "Event Simulator network configuration completed successfully"
        return [PSCustomObject]@{
            DockerNetwork = $dockerResult
            KubernetesNetwork = $k8sResult
            FirewallConfig = $firewallResult
            ConnectivityTest = $testResult
            Status = "Configured"
            Timestamp = Get-Date
        }
    }
    catch {
        Write-Log "Critical error during network configuration: $_" -Level Error
        throw
    }
    finally {
        Stop-Transcript
    }
}

# Export functions
Export-ModuleMember -Function @(
    'Configure-EventSimulatorNetwork',
    'Configure-DockerNetwork',
    'Configure-KubernetesNetwork',
    'Configure-WindowsFirewall',
    'Test-NetworkConnectivity'
)
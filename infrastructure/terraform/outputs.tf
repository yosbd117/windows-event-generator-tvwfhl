# Core Resource Group Outputs
output "resource_group_name" {
  description = "The name of the created resource group"
  value       = resource_group.resource_group_name
}

output "resource_group_location" {
  description = "The location of the resource group"
  value       = resource_group.resource_group_location
}

# Key Vault Outputs
output "key_vault_uri" {
  description = "The URI of the Azure Key Vault"
  value       = resource_group.key_vault_uri
}

# AKS Cluster Outputs
output "aks_cluster_name" {
  description = "The name of the AKS cluster"
  value       = aks.cluster_name
}

output "aks_cluster_id" {
  description = "The resource ID of the AKS cluster"
  value       = aks.cluster_id
}

output "aks_cluster_endpoint" {
  description = "The API server endpoint of the AKS cluster"
  value       = aks.cluster_endpoint
}

output "aks_kube_config" {
  description = "Kubernetes configuration for cluster access"
  value       = aks.kube_config
  sensitive   = true
}

# SQL Server Outputs
output "sql_server_name" {
  description = "The name of the SQL Server"
  value       = sql.server_name
}

output "sql_database_name" {
  description = "The name of the SQL database"
  value       = sql.database_name
}

output "sql_server_fqdn" {
  description = "The fully qualified domain name of the SQL Server"
  value       = sql.server_fqdn
}

output "sql_connection_string" {
  description = "The connection string for the SQL database"
  value       = sql.connection_string
  sensitive   = true
}

# Monitoring Outputs
output "monitoring_workspace_id" {
  description = "The ID of the Log Analytics workspace"
  value       = monitoring.workspace_id
}

output "monitoring_workspace_key" {
  description = "The workspace key for Log Analytics"
  value       = monitoring.workspace_key
  sensitive   = true
}
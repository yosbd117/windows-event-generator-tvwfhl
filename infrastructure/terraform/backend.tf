# Azure Storage Account Backend Configuration for Windows Event Simulator
# Provider Version: hashicorp/azurerm ~> 3.0

terraform {
  backend "azurerm" {
    # Resource group containing the storage account for Terraform state
    resource_group_name = "tfstate-rg"
    
    # Storage account configuration
    storage_account_name = "eventsimtfstate"
    container_name      = "tfstate"
    key                = "windows-event-simulator.tfstate"
    
    # Enhanced security features
    use_azuread_auth    = true
    use_microsoft_graph = true
    storage_use_azuread = true
    enable_blob_encryption = true
    min_tls_version     = "TLS1_2"
    
    # Dynamic subscription and tenant ID retrieval
    subscription_id     = "${data.azurerm_client_config.current.subscription_id}"
    tenant_id          = "${data.azurerm_client_config.current.tenant_id}"
  }
}
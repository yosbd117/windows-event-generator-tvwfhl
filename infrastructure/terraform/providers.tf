# Required providers configuration block
terraform {
  required_providers {
    # Azure Resource Manager provider - version ~> 3.0
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    
    # Azure Active Directory provider - version ~> 2.0
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.0"
    }
  }
}

# Azure Resource Manager provider configuration
provider "azurerm" {
  features {
    # Key Vault security features
    key_vault {
      # Prevent permanent deletion of key vaults and enable soft delete recovery
      purge_soft_delete_on_destroy = false
      recover_soft_deleted_key_vaults = true
      # Prevent permanent deletion of secrets
      purge_soft_deleted_secrets_on_destroy = false
    }

    # Resource group protection features
    resource_group {
      # Prevent accidental deletion of non-empty resource groups
      prevent_deletion_if_contains_resources = true
    }

    # Virtual machine management features
    virtual_machine {
      # Automatically delete OS disks when VM is deleted
      delete_os_disk_on_deletion = true
      # Enable graceful shutdown for VMs
      graceful_shutdown = true
    }

    # Log Analytics workspace retention features
    log_analytics_workspace {
      # Prevent permanent workspace deletion
      permanently_delete_on_destroy = false
    }
  }

  # Enable Azure AD authentication for storage accounts
  storage_use_azuread = true
  
  # Use Managed Service Identity for authentication
  use_msi = true
  
  # Ensure all required providers are registered
  skip_provider_registration = false
  
  # Tenant and subscription configuration
  tenant_id = data.azurerm_client_config.current.tenant_id
  subscription_id = var.subscription_id
}

# Azure Active Directory provider configuration
provider "azuread" {
  # Use Managed Service Identity for authentication
  use_msi = true
  
  # Tenant configuration
  tenant_id = data.azurerm_client_config.current.tenant_id
  
  # Client configuration
  client_id = var.client_id
  
  # Environment configuration
  environment = "public"
  metadata_host = "https://login.microsoftonline.com/"
  
  # Partner ID for resource tracking
  partner_id = var.partner_id
}
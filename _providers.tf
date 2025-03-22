provider azurerm {
  subscription_id = var.subscription_id
  features { }
}

terraform {
  required_version = ">= 1.10"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 4.23.0"
    }
  }
}
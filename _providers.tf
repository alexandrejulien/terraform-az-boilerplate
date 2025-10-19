provider "azurerm" {
  subscription_id = var.subscription_id
  features {}
}

terraform {
  required_version = ">= 1.13"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 4.40.0"
    }
  }
}
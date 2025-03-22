# Terraform Azure Boilerplate

This project provides a Terraform template for deploying resources in Azure. It features a modular structure with separate configuration files for providers, resources, backends, and environment variables.

## Project Structure

- **tf/**: Main Terraform configuration files
  - [`providers.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\providers.tf): Configures the Azure provider and specifies Terraform and provider version requirements.
  - [`rg.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\rg.tf): Defines an Azure Resource Group using supplied variables.
  - [`variables.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\variables.tf): Declares variables for the resource group, such as its name and location.
  - [`backend.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\backend.tf): Specifies the backend configuration for Terraform state management.
- **backends/**: Contains backend configuration files
  - [`backend.nonprod.config`](d:\Dev\Github\terraform-az-boilerplate\backends\backend.nonprod.config): Specifies the Azure Storage backend options for non-production environments.
- **envs/**: Defines environment-specific variable values
  - [`nonprod.tfvars`](d:\Dev\Github\terraform-az-boilerplate\envs\nonprod.tfvars): Contains variable assignments for non-production setups.
- **tasks/**: Holds task definition files for automating common Terraform commands
  - [`TerraformTasks.yml`](d:\Dev\Github\terraform-az-boilerplate\tasks\TerraformTasks.yml): Defines tasks such as initialization, linting, validation, and planning.
- **Taskfile.yml**: Includes the task configuration from `tasks/TerraformTasks.yml` to simplify running project tasks.

## Getting Started

### Prerequisites

- [Terraform](https://www.terraform.io/downloads.html) (version >= 1.10)
- [tflint](https://github.com/terraform-linters/tflint) for linting Terraform files
- Any tool needed to run the tasks (e.g., Task runner as defined in the `Taskfile.yml`)

### Initialization

Initialize the Terraform configuration and backend by running:

```sh
terraform init -backend-config="backends/backend.nonprod.config"
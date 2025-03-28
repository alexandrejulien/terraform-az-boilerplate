# Terraform Azure Boilerplate

![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/alexandrejulien/terraform-az-boilerplate/.github%2Fworkflows%2Frelease.yml)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=sqale_index)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=sqale_index)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=bugs)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)

This project provides a Terraform template for deploying resources in Azure. It features a modular structure with configuration for providers, resource groups, backend state management, environment-specific settings, and tasks to automate common Terraform commands.

**Example :**

```ps1
* default:                       List all tasks
* required-tools:                Required tools
* checkov:scan:                  Checkov scan
* docs:generate:                 📄 Generate terraform docs
* infracosts:default:            💲 Get infra costs report      (aliases: infracosts)
* terraform:apply:               🚀 Terraform apply
* terraform:apply:approve:       🚀 Terraform apply (auto-approve) from plan
* terraform:default:             🚀 Terraform init, and plan      (aliases: terraform)
* terraform:destroy:             ❌ Terraform destroy
* terraform:init:                Terraform init
* terraform:lint:                Lint terraform files
* terraform:plan:                🎯 Terraform plan
* terraform:reconfigure:         Terraform init (reconfigure)
* terraform:unlock:              🔒 Terraform force-unlock
* terraform:upgrade:             Terraform init (upgrade)
* terraform:validate:            ✅ Terraform validate
```

## Project Structure

- **tf/**: Main Terraform configuration files
  - [`providers.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\providers.tf): Configures the Azure provider and specifies the required Terraform and provider versions.
  - [`rg.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\rg.tf): Defines an Azure Resource Group using supplied variables.
  - [`variables.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\variables.tf): Declares variables (e.g., resource group name and location).
  - [`backend.tf`](d:\Dev\Github\terraform-az-boilerplate\tf\backend.tf): Specifies the backend configuration for Terraform state management.
- **modules/**: Reusable Terraform modules
  - Example: [`modules/tags/variables.tf`](d:\Dev\Github\terraform-az-boilerplate\modules\tags\variables.tf): Contains variables and validations for applying conventional tags.
- **backends/**: Contains backend configuration files
  - [`backend.nonprod.config`](d:\Dev\Github\terraform-az-boilerplate\backends\backend.nonprod.config): Provides the Azure Storage backend options for non-production environments.
- **envs/**: Defines environment-specific variable values
  - [`nonprod.tfvars`](d:\Dev\Github\terraform-az-boilerplate\envs\nonprod.tfvars): Contains variable assignments for non-production setups.
- **tasks/**: Holds task definition files for automating common Terraform commands
  - [`TerraformTasks.yml`](d:\Dev\Github\terraform-az-boilerplate\tasks\TerraformTasks.yml): Automates initialization, linting, validation, and planning tasks.
- **Taskfile.yml**: Integrates configuration from `tasks/TerraformTasks.yml` to simplify running project tasks.

## Getting Started

### Prerequisites

- [Terraform](https://www.terraform.io/downloads.html) (version >= 1.10)
- [tflint](https://github.com/terraform-linters/tflint) for linting Terraform files
- A task runner (as referenced in `Taskfile.yml`) to automate commands

### Initialization

Initialize the Terraform configuration and backend by running:

```ps1
task terraform:init
terraform init -backend-config="backends/backend.nonprod.config"
```

### Applying the Configuration

After initialization, you can plan and apply your Terraform configuration:

```sh
task terraform:plan
task terraform:apply 

terraform plan -var-file="envs/nonprod.tfvars"
terraform apply -var-file="envs/nonprod.tfvars"
```

## Troubleshooting

Be sure to check your prerequisites and configurations if you run into issues. For example, verify that your backend configuration and variable files are correctly set up.

## Contributing

Feel free to create issues or submit pull requests with improvements.

## License

This project is licensed under the MIT License.

Happy Terraforming!

---
[![Quality gate](https://sonarcloud.io/api/project_badges/quality_gate?project=alexandrejulien_terraform-az-boilerplate)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
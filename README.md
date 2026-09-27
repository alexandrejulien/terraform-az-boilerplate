# Terraform Azure Boilerplate

![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/alexandrejulien/terraform-az-boilerplate/.github%2Fworkflows%2Frelease.yml)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=sqale_index)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=bugs)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=alexandrejulien_terraform-az-boilerplate&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)

This project provides a Terraform template for deploying resources in Azure. It features a modular structure with configuration for providers, resource groups, backend state management, environment-specific settings, and tasks to automate common Terraform commands.

**Example :**

```ps1
* default:                       List all tasks
* required-tools:                Required tools
* costs:analysis:                💲 Get infra costs report
* docs:generate:                 📄 Generate terraform docs
* security:scan:                 Checkov scan
* tf:apply:                      🚀 Terraform apply                                          (aliases: terraform:apply)
* tf:apply:approve:              🚀 Terraform apply (auto-approve) from plan                 (aliases: terraform:apply:approve)
* tf:default:                    🚀 Terraform init, and plan                                 (aliases: terraform:default, tf, terraform)
* tf:destroy:                    ❌ Terraform destroy                                        (aliases: terraform:destroy)
* tf:init:                       Terraform init                                             (aliases: terraform:init)
* tf:lint:                       Lint terraform files                                       (aliases: terraform:lint)
* tf:plan:                       🎯 Terraform plan                                           (aliases: terraform:plan)
* tf:reconfigure:                Terraform init (reconfigure)                               (aliases: terraform:reconfigure)
* tf:unlock:                     🔒 Terraform force-unlock                                   (aliases: terraform:unlock)
* tf:upgrade:                    Terraform init (upgrade)                                   (aliases: terraform:upgrade)
* tf:validate:                   ✅ Terraform validate                                       (aliases: terraform:validate)
* tf:workspace:create:           Create a new Terraform workspace and make it active in .env  (aliases: terraform:workspace:create)
* tf:workspace:select:           Select a Terraform workspace (writes TF_WORKSPACE to .env)   (aliases: terraform:workspace:select)
* tf:workspace:show:             Show the current Terraform workspace                       (aliases: terraform:workspace:show)
```

## Project Structure

- **/**.tf: Main Terraform configuration files  
  - [`_providers.tf`](_providers.tf): Configures the Azure provider and specifies the required Terraform and provider versions.  
  - [`_backend.tf`](_backend.tf): Declares the `azurerm` remote backend (values come from `backend.tfvars`).  
  - [`_variables.tf`](_variables.tf): Declares variables such as resource group name and location.  
  - [`1_rg.tf`](1_rg.tf): Defines an Azure Resource Group using supplied variables.  
- **modules/**: Reusable Terraform modules  
  - Example: [`modules/tags/variables.tf`](modules/tags/variables.tf): Contains variables and validations for applying conventional tags.
- **environments/**: Environment-specific variable and backend values, one folder per workspace (`nonprod`, `prod`)  
  - [`variables.tfvars`](environments/nonprod/variables.tfvars): Contains variable assignments for the environment.
  - [`backend.tfvars`](environments/nonprod/backend.tfvars): Contains the remote state backend definition for the environment.
- **.tasks/**: Task definition files for automating common commands  
  - [`TerraformTasks.yml`](.tasks/TerraformTasks.yml): Automates initialization, linting, validation, planning and apply tasks.
- **Taskfile.yml**: Includes the `.tasks/*.yml` files to simplify running project tasks.

## Getting Started

### Prerequisites

Works only on Windows Systems.
Multi-OS work in progress.

- [Terraform](https://www.terraform.io/downloads.html) (version >= 1.10)
- [tflint](https://github.com/terraform-linters/tflint) for linting Terraform files
- A task runner (as referenced in `Taskfile.yml`) to automate commands
- Chocolatey or winget to install requirements packages

### Initialization

Initialize the Terraform configuration and backend by running:

```ps1
task tf:init
# Execute :
terraform init -backend-config="environments\nonprod\backend.tfvars"
```

### Applying the Configuration

After initialization, you can plan and apply your Terraform configuration. Use your task runner or run the commands manually:

```sh
task tf:plan
task tf:apply           # interactive apply
task tf:apply:approve   # applies the saved plan.tfplan exactly as reviewed

# Execute:
terraform plan --var-file "environments\nonprod\variables.tfvars" --out plan.tfplan
terraform apply --var-file "environments\nonprod\variables.tfvars"
terraform apply -auto-approve plan.tfplan
```

### Multi-environment

Variables and backend values live in `environments/<workspace>/*.tfvars`.

The active environment is set by `TF_WORKSPACE` in the `.env` file. Terraform reads this variable natively, and the tasks use it to pick the right `environments/<workspace>` folder. Because `TF_WORKSPACE` overrides `terraform workspace select`, switch environments through the tasks, which update `.env`:

```ps1
task tf:workspace:select NAME=prod   # existing workspace
task tf:workspace:create NAME=qa     # new workspace (requires environments/qa/)
```

Inside the configuration, use `terraform.workspace` to vary behaviour per environment:

```hcl
resource "azurerm_resource_group" "my_resource" {
  count = terraform.workspace == "nonprod" ? 1 : 0
  # ...
}
```

## Troubleshooting

Ensure you check your prerequisites and configurations if you run into issues. Validate that your backend configuration and variable files are set up correctly.

## Contributing

Feel free to create issues or submit pull requests with improvements.

## License

This project is licensed under the MIT License.

Happy Terraforming!

---
[![Quality gate](https://sonarcloud.io/api/project_badges/quality_gate?project=alexandrejulien_terraform-az-boilerplate)](https://sonarcloud.io/summary/new_code?id=alexandrejulien_terraform-az-boilerplate)
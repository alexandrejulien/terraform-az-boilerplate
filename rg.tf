module "main_tags"{
    source = "./modules/tags"
    conventional_tags = {
        Application = "MyApp"
        Environment = "Dev"
    }
}

resource "azurerm_resource_group" "rg" {
    name = var.resource_group_name
    location = var.location

    tags = module.main_tags.conventional_tags
}
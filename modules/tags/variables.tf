variable "conventional_tags" {
  type    = map(string)
  default = {}

  validation {
    condition     = contains(keys(var.conventional_tags), "Application")
    error_message = "Invalid conventional tags: key 'Application' is required."
  }
}
namespace TfStudio.Server.Project;

/// <summary>
/// The boilerplate conventions TF Studio depends on. Keep in sync with .tasks/TerraformTasks.yml
/// (OUTPUT_PLAN / OUTPUT_GRAPH, task names) and the environments/&lt;workspace&gt;/ layout.
/// </summary>
public static class ProjectLayout
{
    public const string EnvFile = ".env";
    public const string WorkspaceVariable = "TF_WORKSPACE";
    public const string EnvironmentsDirectory = "environments";
    public const string BackendVarsFile = "backend.tfvars";
    public const string VariablesVarsFile = "variables.tfvars";
    public const string LocalVarsFile = "local.tfvars";
    public const string PlanFile = "plan.tfplan";
    public const string PlanJsonFile = "plan.tfgraph";
    public const string BackendMetadataFile = ".terraform/terraform.tfstate";
}

public static class TaskNames
{
    public const string Init = "tf:init";
    public const string Plan = "tf:plan";
    public const string Apply = "tf:apply";
    public const string ApplySavedPlan = "tf:apply:approve";
    public const string Destroy = "tf:destroy";
    public const string Unlock = "tf:unlock";
    public const string WorkspaceCreate = "tf:workspace:create";
    public const string WorkspaceSelect = "tf:workspace:select";
}

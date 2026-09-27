using System.Text.Json;

namespace TfStudio.Server.Terraform;

// Shapes of the JSON produced by external tools. Only the fields TF Studio reads are declared.

/// <summary><c>task --list-all --json</c></summary>
public sealed class TaskListJson
{
    public List<TaskJson>? Tasks { get; set; }
}

public sealed class TaskJson
{
    public string Name { get; set; } = "";
    public string? Desc { get; set; }
    public string? Summary { get; set; }
    public List<string>? Aliases { get; set; }
}

/// <summary><c>terraform show -json plan.tfplan</c> (written to plan.tfgraph by <c>task tf:plan</c>).</summary>
public sealed class TfPlanJson
{
    public string? FormatVersion { get; set; }
    public string? TerraformVersion { get; set; }
    public string? Timestamp { get; set; }
    public bool Errored { get; set; }
    public bool? Applyable { get; set; }
    public bool? Complete { get; set; }
    public List<TfResourceChangeJson>? ResourceChanges { get; set; }
    public List<TfResourceChangeJson>? ResourceDrift { get; set; }
    public Dictionary<string, TfChangeJson>? OutputChanges { get; set; }
}

public sealed class TfResourceChangeJson
{
    public string Address { get; set; } = "";
    public string? PreviousAddress { get; set; }
    public string? ModuleAddress { get; set; }
    public string? Mode { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? ProviderName { get; set; }
    public string? ActionReason { get; set; }
    public TfChangeJson? Change { get; set; }
}

/// <summary>
/// before/after hold arbitrary attribute trees. after_unknown / *_sensitive mirror them with <c>true</c>
/// at unknown/sensitive nodes (or are a bare boolean for outputs). Missing members stay
/// <see cref="JsonValueKind.Undefined"/>.
/// </summary>
public sealed class TfChangeJson
{
    public List<string>? Actions { get; set; }
    public JsonElement Before { get; set; }
    public JsonElement After { get; set; }
    public JsonElement AfterUnknown { get; set; }
    public JsonElement BeforeSensitive { get; set; }
    public JsonElement AfterSensitive { get; set; }
    public JsonElement ReplacePaths { get; set; }
    public TfImportingJson? Importing { get; set; }
}

public sealed class TfImportingJson
{
    public string? Id { get; set; }
}

/// <summary>.terraform/terraform.tfstate — backend metadata written by <c>terraform init</c> (not real state).</summary>
public sealed class TfBackendStateJson
{
    public TfBackendJson? Backend { get; set; }
}

public sealed class TfBackendJson
{
    public string? Type { get; set; }
    public Dictionary<string, JsonElement>? Config { get; set; }
}

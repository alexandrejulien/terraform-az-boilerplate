using System.Text.Json.Serialization;

namespace TfStudio.Server.Api;

// ---- Requests -------------------------------------------------------------------------------

public sealed record RunRequest(List<string> Tasks, Dictionary<string, string>? Vars);

public sealed record InputRequest(string Text);

// ---- Common ---------------------------------------------------------------------------------

public sealed record ApiError(string Error);

public sealed record HealthResponse(string Status, string Version, string ProjectRoot);

// ---- Project --------------------------------------------------------------------------------

public sealed record ProjectInfo(
    string Root,
    string Name,
    string? Workspace,
    IReadOnlyList<EnvironmentInfo> Environments,
    BackendStatus Backend,
    PlanFilesInfo PlanFiles,
    IReadOnlyList<string> Warnings);

public sealed record EnvironmentInfo(string Name, bool HasBackend, bool HasVariables, bool HasLocalVariables, bool IsActive);

public sealed record EnvironmentFiles(string Name, string? Backend, string? Variables);

/// <summary>Whether the backend initialized in .terraform/ matches the active environment.</summary>
/// <param name="State">ok | not-initialized | mismatch | unknown</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="MismatchedKeys">Key names only — values may be secrets.</param>
public sealed record BackendStatus(string State, string Message, IReadOnlyList<string> MismatchedKeys);

public sealed record PlanFilesInfo(bool PlanExists, DateTimeOffset? PlanModifiedAt, bool JsonExists, DateTimeOffset? JsonModifiedAt);

public sealed record TaskInfo(
    string Name,
    string Description,
    string? Summary,
    IReadOnlyList<string> Aliases,
    string Namespace,
    bool Dangerous,
    bool Interactive,
    IReadOnlyList<string> RequiredVars);

public sealed record ToolStatus(
    string Name,
    string Title,
    bool Required,
    string UsedBy,
    string InstallHint,
    bool Found,
    string? Path,
    string? Version,
    string? Error);

// ---- Runs -----------------------------------------------------------------------------------

[JsonConverter(typeof(JsonStringEnumConverter<RunState>))]
public enum RunState
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("running")] Running,
    [JsonStringEnumMemberName("succeeded")] Succeeded,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
    [JsonStringEnumMemberName("skipped")] Skipped,
}

public sealed record RunSummary(
    string Id,
    IReadOnlyList<RunStepSummary> Steps,
    Dictionary<string, string> Vars,
    string? Workspace,
    RunState State,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    bool AcceptsInput);

public sealed record RunStepSummary(string Task, RunState State, int? ExitCode, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt);

/// <summary>One entry of a run's event log, streamed over SSE.</summary>
/// <param name="Seq">Monotonic sequence number, used to resume a stream.</param>
/// <param name="Type">output | status | trimmed</param>
/// <param name="Stream">stdout | stderr | system | stdin (output events only)</param>
/// <param name="Text">Output text (output events only).</param>
/// <param name="Run">Run snapshot (status events only).</param>
public sealed record RunEvent(long Seq, string Type, string? Stream = null, string? Text = null, RunSummary? Run = null);

// ---- Plan -----------------------------------------------------------------------------------

public sealed record PlanReport(
    PlanFilesInfo Files,
    string? TerraformVersion,
    string? FormatVersion,
    string? Timestamp,
    bool Errored,
    string? PlanWorkspace,
    string? CurrentWorkspace,
    bool OriginKnown,
    bool AlreadyApplied,
    bool HasChanges,
    bool CanApply,
    PlanSummary Summary,
    IReadOnlyList<ResourceChangeView> Resources,
    IReadOnlyList<OutputChangeView> Outputs,
    IReadOnlyList<ResourceChangeView> Drift,
    IReadOnlyList<PlanNotice> Notices);

/// <summary>Add/Change/Destroy match Terraform's "Plan: X to add, Y to change, Z to destroy." line.</summary>
public sealed record PlanSummary(
    int Add, int Change, int Destroy,
    int Create, int Update, int Delete, int Replace, int Read, int NoOp, int Import, int Move, int Forget);

/// <summary>One resource from the plan's resource_changes (or resource_drift).</summary>
/// <param name="Action">create | update | delete | replace | read | no-op | forget</param>
/// <param name="ReplaceOrder">delete-before-create | create-before-destroy (replace only)</param>
public sealed record ResourceChangeView(
    string Address,
    string? ModuleAddress,
    string? Mode,
    string? Type,
    string? Name,
    string? ProviderName,
    string Action,
    string? ReplaceOrder,
    string? ActionReason,
    string? ImportId,
    string? PreviousAddress,
    int ChangedAttributes,
    IReadOnlyList<AttributeDiff> Attributes);

/// <summary>Before/After are display strings (null = absent). Sensitive values are masked server-side.</summary>
public sealed record AttributeDiff(
    string Path, string? Before, string? After, bool Changed, bool Unknown, bool Sensitive, bool ForcesReplacement);

public sealed record OutputChangeView(string Name, string Action, string? Before, string? After, bool Sensitive, bool Unknown);

/// <summary>A message shown above the plan (staleness, workspace mismatch, ...).</summary>
/// <param name="Level">info | warning | danger</param>
public sealed record PlanNotice(string Level, string Message);

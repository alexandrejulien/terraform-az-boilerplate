using System.Text;
using System.Text.Json;
using TfStudio.Server.Api;
using TfStudio.Server.Infrastructure;
using TfStudio.Server.Project;
using TfStudio.Server.Runs;
using TfStudio.Server.Terraform;

namespace TfStudio.Server.Plan;

/// <summary>
/// Builds the plan review model from plan.tfgraph (the JSON rendering of plan.tfplan that
/// `task tf:plan` writes), plus safety checks before `tf:apply:approve`.
/// </summary>
public sealed class PlanService(StudioOptions options, ProjectService project, RunManager runs)
{
    // tf:plan writes plan.tfplan then plan.tfgraph a moment later; beyond this the JSON is from an older plan.
    private static readonly TimeSpan StaleTolerance = TimeSpan.FromSeconds(2);

    public async Task<PlanReport?> GetReportAsync(CancellationToken cancellationToken)
    {
        var files = project.GetPlanFiles();
        if (!files.JsonExists)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(Path.Combine(options.ProjectRoot, ProjectLayout.PlanJsonFile), cancellationToken);
        var plan = Parse(bytes);
        var notices = new List<PlanNotice>();

        var currentWorkspace = project.ReadActiveWorkspace();
        var stamp = runs.LastPlan;
        var originKnown = stamp is not null && files.PlanModifiedAt?.UtcDateTime == stamp.PlanWriteTimeUtc;
        var planWorkspace = originKnown ? stamp!.Workspace : null;
        var alreadyApplied = files.PlanModifiedAt is { } planTime && runs.LastAppliedPlanWriteTime == planTime.UtcDateTime;
        var stale = files.PlanModifiedAt is { } p && files.JsonModifiedAt is { } j && j < p - StaleTolerance;

        if (!files.PlanExists)
        {
            notices.Add(new("warning", $"{ProjectLayout.PlanFile} is missing: this report can't be applied. Run {TaskNames.Plan}."));
        }

        if (stale)
        {
            notices.Add(new("warning", $"{ProjectLayout.PlanJsonFile} is older than {ProjectLayout.PlanFile}: this report may not match the saved plan. Re-run {TaskNames.Plan}."));
        }

        if (plan.Errored)
        {
            notices.Add(new("danger", "Terraform reported errors while planning: this plan is incomplete and can't be applied."));
        }

        if (originKnown && planWorkspace != currentWorkspace)
        {
            notices.Add(new("danger", $"This plan was made for workspace '{planWorkspace}', but the active workspace is '{currentWorkspace}'."));
        }
        else if (!originKnown && files.PlanExists)
        {
            notices.Add(new("info", $"This plan wasn't created by TF Studio in this session, so its workspace can't be verified. Re-run {TaskNames.Plan} to be sure it targets '{currentWorkspace}'."));
        }

        if (alreadyApplied)
        {
            notices.Add(new("info", $"This plan has already been applied. Run {TaskNames.Plan} again to see remaining changes."));
        }

        var resources = (plan.ResourceChanges ?? []).Where(r => r.Change is not null).Select(ToView).ToList();
        var drift = (plan.ResourceDrift ?? []).Where(r => r.Change is not null).Select(ToView).ToList();
        var outputs = (plan.OutputChanges ?? new())
            .Select(o => PlanDiffBuilder.BuildOutput(o.Key, Classify(o.Value.Actions).Action, o.Value))
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
        var summary = Summarize(resources);

        var hasChanges = summary.Add + summary.Change + summary.Destroy + summary.Import + summary.Forget + summary.Move > 0
            || outputs.Any(o => o.Action != "no-op");
        var canApply = files.PlanExists && !stale && !plan.Errored && plan.Applyable != false && !alreadyApplied
            && hasChanges && (!originKnown || planWorkspace == currentWorkspace);

        return new PlanReport(
            files,
            plan.TerraformVersion,
            plan.FormatVersion,
            plan.Timestamp,
            plan.Errored,
            planWorkspace,
            currentWorkspace,
            originKnown,
            alreadyApplied,
            hasChanges,
            canApply,
            summary,
            resources,
            outputs,
            drift,
            notices);
    }

    private static TfPlanJson Parse(byte[] bytes)
    {
        try
        {
            // `terraform show -json > plan.tfgraph` from Windows PowerShell 5.1 writes UTF-16; Task writes UTF-8.
            if (bytes is [0xFF, 0xFE, ..])
            {
                return JsonSerializer.Deserialize(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), ExternalJsonContext.Default.TfPlanJson)
                    ?? throw new JsonException("empty document");
            }

            ReadOnlySpan<byte> utf8 = bytes is [0xEF, 0xBB, 0xBF, ..] ? bytes.AsSpan(3) : bytes;
            return JsonSerializer.Deserialize(utf8, ExternalJsonContext.Default.TfPlanJson)
                ?? throw new JsonException("empty document");
        }
        catch (JsonException ex)
        {
            throw new StudioException(
                $"{ProjectLayout.PlanJsonFile} is not valid `terraform show -json` output ({ex.Message}). Re-run {TaskNames.Plan}.");
        }
    }

    private static ResourceChangeView ToView(TfResourceChangeJson resource)
    {
        var change = resource.Change!;
        var (action, replaceOrder) = Classify(change.Actions);
        var importing = change.Importing is not null;
        var moved = resource.PreviousAddress is not null && resource.PreviousAddress != resource.Address;

        // A pure no-op has nothing to show; imports and moves still get their attributes for context.
        var attributes = action == "no-op" && !importing && !moved ? [] : PlanDiffBuilder.Build(change);

        return new ResourceChangeView(
            resource.Address,
            resource.ModuleAddress,
            resource.Mode,
            resource.Type,
            resource.Name,
            resource.ProviderName,
            action,
            replaceOrder,
            resource.ActionReason,
            change.Importing?.Id ?? (importing ? "" : null),
            moved ? resource.PreviousAddress : null,
            attributes.Count(a => a.Changed),
            attributes);
    }

    private static (string Action, string? ReplaceOrder) Classify(List<string>? actions) => actions switch
    {
        ["no-op"] => ("no-op", null),
        ["create"] => ("create", null),
        ["read"] => ("read", null),
        ["update"] => ("update", null),
        ["delete"] => ("delete", null),
        ["forget"] => ("forget", null),
        ["delete", "create"] => ("replace", "delete-before-create"),
        ["create", "delete"] => ("replace", "create-before-destroy"),
        null or [] => ("no-op", null),
        _ => (string.Join("+", actions), null),
    };

    private static PlanSummary Summarize(List<ResourceChangeView> resources)
    {
        int Count(string action) => resources.Count(r => r.Action == action);

        var create = Count("create");
        var update = Count("update");
        var delete = Count("delete");
        var replace = Count("replace");
        return new PlanSummary(
            Add: create + replace,
            Change: update,
            Destroy: delete + replace,
            create,
            update,
            delete,
            replace,
            Read: Count("read"),
            NoOp: Count("no-op"),
            Import: resources.Count(r => r.ImportId is not null),
            Move: resources.Count(r => r.PreviousAddress is not null),
            Forget: Count("forget"));
    }
}

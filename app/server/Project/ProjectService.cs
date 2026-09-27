using System.Text.Json;
using System.Text.RegularExpressions;
using TfStudio.Server.Api;
using TfStudio.Server.Infrastructure;
using TfStudio.Server.Terraform;

namespace TfStudio.Server.Project;

/// <summary>Read-only view of the boilerplate project on disk: .env, environments/, backend init state, plan files.</summary>
public sealed partial class ProjectService(StudioOptions options)
{
    public string Root => options.ProjectRoot;

    public ProjectInfo GetInfo()
    {
        var workspace = ReadActiveWorkspace();
        var environments = ListEnvironments(workspace);
        var warnings = new List<string>();

        if (workspace is null)
        {
            warnings.Add($"No {ProjectLayout.WorkspaceVariable} in {ProjectLayout.EnvFile}: select a workspace before running Terraform tasks.");
        }
        else if (!environments.Any(e => e.IsActive))
        {
            warnings.Add($"Active workspace '{workspace}' has no {ProjectLayout.EnvironmentsDirectory}/{workspace}/ folder.");
        }

        var inherited = Environment.GetEnvironmentVariable(ProjectLayout.WorkspaceVariable);
        if (!string.IsNullOrEmpty(inherited) && inherited != workspace)
        {
            warnings.Add(
                $"{ProjectLayout.WorkspaceVariable}={inherited} is set in the environment TF Studio was launched from. " +
                $"TF Studio ignores it and uses {ProjectLayout.EnvFile} ({workspace ?? "unset"}), but plain `task` runs from that shell would target '{inherited}'.");
        }

        return new ProjectInfo(
            Root,
            new DirectoryInfo(Root).Name,
            workspace,
            environments,
            GetBackendStatus(workspace),
            GetPlanFiles(),
            warnings);
    }

    /// <summary>TF_WORKSPACE from .env — the single source of truth used by Taskfile and Terraform.</summary>
    public string? ReadActiveWorkspace()
    {
        var path = Path.Combine(Root, ProjectLayout.EnvFile);
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var equals = line.IndexOf('=');
            if (line.StartsWith('#') || equals <= 0 || line[..equals].Trim() != ProjectLayout.WorkspaceVariable)
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim().Trim('"', '\'');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    public IReadOnlyList<EnvironmentInfo> ListEnvironments(string? activeWorkspace)
    {
        var directory = Path.Combine(Root, ProjectLayout.EnvironmentsDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(directory)
            .Select(path => new EnvironmentInfo(
                Path.GetFileName(path),
                File.Exists(Path.Combine(path, ProjectLayout.BackendVarsFile)),
                File.Exists(Path.Combine(path, ProjectLayout.VariablesVarsFile)),
                File.Exists(Path.Combine(path, ProjectLayout.LocalVarsFile)),
                string.Equals(Path.GetFileName(path), activeWorkspace, StringComparison.Ordinal)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns the committed tfvars of an environment. local.tfvars (secrets) is never exposed.</summary>
    public EnvironmentFiles? ReadEnvironmentFiles(string name)
    {
        if (!EnvironmentNameRegex().IsMatch(name))
        {
            return null;
        }

        var directory = Path.Combine(Root, ProjectLayout.EnvironmentsDirectory, name);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return new EnvironmentFiles(
            name,
            ReadIfExists(Path.Combine(directory, ProjectLayout.BackendVarsFile)),
            ReadIfExists(Path.Combine(directory, ProjectLayout.VariablesVarsFile)));
    }

    /// <summary>
    /// Compares the active environment's backend.tfvars with what `terraform init` last recorded in
    /// .terraform/terraform.tfstate. Environments may point at different state storage, so switching
    /// workspace can require `tf:reconfigure`.
    /// </summary>
    public BackendStatus GetBackendStatus(string? workspace)
    {
        if (workspace is null)
        {
            return new BackendStatus("unknown", "No active workspace.", []);
        }

        var backendVars = Path.Combine(Root, ProjectLayout.EnvironmentsDirectory, workspace, ProjectLayout.BackendVarsFile);
        if (!File.Exists(backendVars))
        {
            return new BackendStatus("unknown", $"{ProjectLayout.EnvironmentsDirectory}/{workspace}/{ProjectLayout.BackendVarsFile} not found.", []);
        }

        var metadataPath = Path.Combine(Root, ProjectLayout.BackendMetadataFile);
        if (!File.Exists(metadataPath))
        {
            return new BackendStatus("not-initialized", "Terraform is not initialized in this folder. Run tf:init.", []);
        }

        TfBackendStateJson? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize(File.ReadAllBytes(metadataPath), ExternalJsonContext.Default.TfBackendStateJson);
        }
        catch (JsonException)
        {
            return new BackendStatus("unknown", $"Could not parse {ProjectLayout.BackendMetadataFile}.", []);
        }

        var recorded = metadata?.Backend?.Config;
        if (recorded is null)
        {
            return new BackendStatus("not-initialized", "No backend recorded by terraform init. Run tf:init.", []);
        }

        var expected = TfvarsParser.Parse(File.ReadAllText(backendVars));
        var mismatched = expected
            .Where(pair => !recorded.TryGetValue(pair.Key, out var actual) || AsText(actual) != pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        return mismatched.Count == 0
            ? new BackendStatus("ok", $"Backend initialized with {ProjectLayout.EnvironmentsDirectory}/{workspace}/{ProjectLayout.BackendVarsFile}.", [])
            : new BackendStatus("mismatch", $"The initialized backend differs from '{workspace}'. Run tf:reconfigure before planning.", mismatched);
    }

    public PlanFilesInfo GetPlanFiles()
    {
        var plan = new FileInfo(Path.Combine(Root, ProjectLayout.PlanFile));
        var json = new FileInfo(Path.Combine(Root, ProjectLayout.PlanJsonFile));
        return new PlanFilesInfo(
            plan.Exists,
            plan.Exists ? plan.LastWriteTimeUtc : null,
            json.Exists,
            json.Exists ? json.LastWriteTimeUtc : null);
    }

    private static string? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static string? AsText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex EnvironmentNameRegex();
}

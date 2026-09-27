using TfStudio.Server.Api;
using TfStudio.Server.Infrastructure;

namespace TfStudio.Server.Project;

/// <summary>Checks the CLI tools the Taskfile relies on (mirrors .tasks/InstallTasks.yml).</summary>
public sealed class ToolsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private sealed record ToolDefinition(string Command, string Title, bool Required, string UsedBy, string InstallHint);

    private readonly string _workingDirectory;
    private readonly ToolDefinition[] _tools;
    private (DateTimeOffset CheckedAt, IReadOnlyList<ToolStatus> Tools)? _cache;

    public ToolsService(StudioOptions options)
    {
        _workingDirectory = options.ProjectRoot;
        _tools =
        [
            new(options.TaskExecutable, "Task (go-task)", true, "every action", "winget install Task.Task"),
            new("terraform", "Terraform", true, "tf:*", "winget install Hashicorp.Terraform"),
            new("tflint", "TFLint", false, "tf:lint", "winget install TerraformLinters.tflint"),
            new("terraform-docs", "terraform-docs", false, "docs:generate", "winget install Terraform-docs.Terraform-docs"),
            new("checkov", "Checkov", false, "security:scan", "pip install checkov"),
            new("infracost", "Infracost", false, "costs:analysis", "winget install Infracost.Infracost"),
        ];
    }

    public async Task<IReadOnlyList<ToolStatus>> CheckAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && _cache is { } cached && DateTimeOffset.UtcNow - cached.CheckedAt < CacheDuration)
        {
            return cached.Tools;
        }

        var tools = await Task.WhenAll(_tools.Select(tool => CheckAsync(tool, cancellationToken)));
        _cache = (DateTimeOffset.UtcNow, tools);
        return tools;
    }

    private async Task<ToolStatus> CheckAsync(ToolDefinition tool, CancellationToken cancellationToken)
    {
        var path = ExecutableLocator.Find(tool.Command);
        if (path is null)
        {
            return Status(tool, path: null, version: null, error: "Not found on PATH");
        }

        try
        {
            var result = await ProcessRunner.RunAsync(
                path, ["--version"], _workingDirectory, TimeSpan.FromSeconds(20), cancellationToken);
            var firstLine = (result.StandardOutput + "\n" + result.StandardError)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return result.ExitCode == 0
                ? Status(tool, path, firstLine, error: null)
                : Status(tool, path, version: null, error: firstLine ?? $"exit code {result.ExitCode}");
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Status(tool, path, version: null, error: ex.Message);
        }
    }

    private static ToolStatus Status(ToolDefinition tool, string? path, string? version, string? error) =>
        new(Path.GetFileNameWithoutExtension(tool.Command), tool.Title, tool.Required, tool.UsedBy, tool.InstallHint,
            path is not null && error is null, path, version, error);
}

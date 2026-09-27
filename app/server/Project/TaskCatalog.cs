using System.Text.Json;
using TfStudio.Server.Api;
using TfStudio.Server.Infrastructure;
using TfStudio.Server.Terraform;

namespace TfStudio.Server.Project;

/// <summary>
/// The Taskfile is the source of truth for what can run: tasks are discovered with
/// <c>task --list-all --json</c>, so forks that add tasks get them in the UI for free.
/// </summary>
public sealed class TaskCatalog(StudioOptions options)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    // UX policy on top of the Taskfile: which tasks need a confirmation, prompt on stdin, or take vars.
    private static readonly HashSet<string> DangerousTasks =
        [TaskNames.Apply, TaskNames.ApplySavedPlan, TaskNames.Destroy, TaskNames.Unlock];

    private static readonly HashSet<string> InteractiveTasks = [TaskNames.Apply, TaskNames.Destroy];

    private static readonly Dictionary<string, string[]> RequiredVars = new(StringComparer.Ordinal)
    {
        [TaskNames.Unlock] = ["ID"],
        [TaskNames.WorkspaceCreate] = ["NAME"],
        [TaskNames.WorkspaceSelect] = ["NAME"],
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset LoadedAt, IReadOnlyList<TaskInfo> Tasks)? _cache;

    public async Task<IReadOnlyList<TaskInfo>> GetTasksAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } cached && DateTimeOffset.UtcNow - cached.LoadedAt < CacheDuration)
            {
                return cached.Tasks;
            }

            var tasks = await LoadAsync(cancellationToken);
            _cache = (DateTimeOffset.UtcNow, tasks);
            return tasks;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Resolves a task by name or alias (e.g. terraform:plan → tf:plan).</summary>
    public async Task<TaskInfo?> ResolveAsync(string nameOrAlias, CancellationToken cancellationToken)
    {
        var tasks = await GetTasksAsync(cancellationToken);
        return tasks.FirstOrDefault(t => t.Name == nameOrAlias) ?? tasks.FirstOrDefault(t => t.Aliases.Contains(nameOrAlias));
    }

    private async Task<IReadOnlyList<TaskInfo>> LoadAsync(CancellationToken cancellationToken)
    {
        var executable = ExecutableLocator.Find(options.TaskExecutable)
            ?? throw new StudioException(StudioErrorKind.Unavailable, $"'{options.TaskExecutable}' (go-task) was not found on PATH.");

        var result = await ProcessRunner.RunAsync(
            executable, ["--list-all", "--json"], options.ProjectRoot, TimeSpan.FromSeconds(30), cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new StudioException(StudioErrorKind.Unavailable, $"task --list-all failed: {result.StandardError.Trim()}");
        }

        TaskListJson? list;
        try
        {
            list = JsonSerializer.Deserialize(result.StandardOutput, ExternalJsonContext.Default.TaskListJson);
        }
        catch (JsonException ex)
        {
            throw new StudioException(StudioErrorKind.Unavailable, $"Unexpected output from task --list-all --json: {ex.Message}");
        }

        return (list?.Tasks ?? [])
            // app:* builds/launches TF Studio itself (.tasks/AppTasks.yml): not something to run from inside it.
            .Where(t => t.Name.Length > 0 && !t.Name.StartsWith("app:", StringComparison.Ordinal))
            .Select(t => new TaskInfo(
                t.Name,
                t.Desc ?? "",
                string.IsNullOrWhiteSpace(t.Summary) ? null : t.Summary,
                t.Aliases ?? [],
                t.Name.Contains(':') ? t.Name[..t.Name.IndexOf(':')] : "",
                DangerousTasks.Contains(t.Name),
                InteractiveTasks.Contains(t.Name),
                RequiredVars.GetValueOrDefault(t.Name, [])))
            .ToList();
    }
}

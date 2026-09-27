using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TfStudio.Server.Api;
using TfStudio.Server.Infrastructure;
using TfStudio.Server.Project;

namespace TfStudio.Server.Runs;

/// <summary>Where the current plan.tfplan came from, captured when a tf:plan step succeeds.</summary>
public sealed record PlanStamp(string RunId, string? Workspace, DateTime PlanWriteTimeUtc);

/// <summary>
/// Executes Task steps as child processes, one run at a time (Terraform state locking makes
/// concurrent runs pointless at best). Keeps a bounded in-memory history.
/// </summary>
public sealed partial class RunManager(
    StudioOptions options, ProjectService project, TaskCatalog catalog, ILogger<RunManager> logger)
{
    private const int MaxHistory = 50;
    private const int MaxSteps = 10;

    private readonly Lock _lock = new();
    private readonly List<Run> _runs = [];
    private Run? _active;
    private PlanStamp? _lastPlan;
    private DateTime? _lastAppliedPlanWriteTime;

    public PlanStamp? LastPlan
    {
        get { lock (_lock) { return _lastPlan; } }
    }

    public DateTime? LastAppliedPlanWriteTime
    {
        get { lock (_lock) { return _lastAppliedPlanWriteTime; } }
    }

    public IReadOnlyList<RunSummary> List()
    {
        lock (_lock)
        {
            return _runs.AsEnumerable().Reverse().Select(r => r.ToSummary()).ToList();
        }
    }

    public Run? Get(string id)
    {
        lock (_lock)
        {
            return _runs.FirstOrDefault(r => r.Id == id);
        }
    }

    public async Task<Run> StartAsync(RunRequest request, CancellationToken cancellationToken)
    {
        if (request.Tasks is not { Count: > 0 and <= MaxSteps })
        {
            throw new StudioException($"Provide between 1 and {MaxSteps} tasks.");
        }

        var vars = ValidateVars(request.Vars);
        var tasks = new List<TaskInfo>();
        foreach (var name in request.Tasks)
        {
            var task = await catalog.ResolveAsync(name, cancellationToken)
                ?? throw new StudioException($"Unknown task '{name}'. It must exist in the project's Taskfile.");
            var missing = task.RequiredVars.Where(v => !vars.ContainsKey(v)).ToList();
            if (missing.Count > 0)
            {
                throw new StudioException($"{task.Name} requires {string.Join(", ", missing)}.");
            }

            tasks.Add(task);
        }

        var executable = ExecutableLocator.Find(options.TaskExecutable)
            ?? throw new StudioException($"'{options.TaskExecutable}' (go-task) was not found on PATH.");
        var workspace = project.ReadActiveWorkspace();
        if (tasks.Any(t => t.Name == TaskNames.ApplySavedPlan))
        {
            GuardSavedPlanApply(workspace);
        }

        Run run;
        lock (_lock)
        {
            if (_active is { IsFinished: false } active)
            {
                throw new StudioException(
                    StudioErrorKind.Conflict, $"Run {active.Id} is still in progress. Wait for it or cancel it first.");
            }

            run = new Run(NewRunId(), tasks.Select(t => t.Name).ToList(), vars, workspace);
            _active = run;
            _runs.Add(run);
            TrimHistory();
        }

        // Not tied to the HTTP request: the run outlives it.
        _ = Task.Run(() => ExecuteAsync(run, executable), CancellationToken.None);
        return run;
    }

    public async Task SendInputAsync(string runId, string text)
    {
        var run = Get(runId) ?? throw new StudioException(StudioErrorKind.NotFound, $"Run '{runId}' not found.");
        var process = run.CurrentProcess;
        if (process is null || run.IsFinished)
        {
            throw new StudioException("This run is not waiting for input.");
        }

        try
        {
            await process.StandardInput.WriteLineAsync(text);
            await process.StandardInput.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new StudioException("The process is no longer accepting input.");
        }

        // Confirmations are echoed; anything else could be a secret typed at a variable prompt.
        var echo = text.Trim() is "yes" or "no" ? text.Trim() : $"‹{text.Length} characters sent›";
        run.Append(OutputStreams.Stdin, $"> {echo}\n");
    }

    public void Cancel(string runId)
    {
        var run = Get(runId) ?? throw new StudioException(StudioErrorKind.NotFound, $"Run '{runId}' not found.");
        if (!run.IsFinished)
        {
            run.Cancellation.Cancel();
        }
    }

    /// <summary>Server shutdown: don't leave terraform running without a parent.</summary>
    public void KillActive()
    {
        Run? active;
        lock (_lock)
        {
            active = _active;
        }

        if (active is { IsFinished: false })
        {
            active.Cancellation.Cancel();
            if (active.CurrentProcess is { } process)
            {
                ChildProcess.KillTree(process);
            }
        }
    }

    private void GuardSavedPlanApply(string? workspace)
    {
        var planPath = Path.Combine(options.ProjectRoot, ProjectLayout.PlanFile);
        if (!File.Exists(planPath))
        {
            throw new StudioException($"No {ProjectLayout.PlanFile} found. Run {TaskNames.Plan} first.");
        }

        var writeTime = File.GetLastWriteTimeUtc(planPath);
        lock (_lock)
        {
            if (_lastPlan is { } stamp && stamp.PlanWriteTimeUtc == writeTime && stamp.Workspace != workspace)
            {
                throw new StudioException(
                    $"{ProjectLayout.PlanFile} was created for workspace '{stamp.Workspace}' but the active workspace is '{workspace}'. Re-run {TaskNames.Plan}.");
            }

            if (_lastAppliedPlanWriteTime == writeTime)
            {
                throw new StudioException($"This plan has already been applied. Re-run {TaskNames.Plan}.");
            }
        }
    }

    private async Task ExecuteAsync(Run run, string executable)
    {
        var finalState = RunState.Succeeded;
        int? lastExitCode = null;
        try
        {
            for (var index = 0; index < run.Tasks.Count; index++)
            {
                var task = run.Tasks[index];
                if (run.Cancellation.IsCancellationRequested)
                {
                    finalState = RunState.Cancelled;
                    break;
                }

                var planWriteTimeBefore = PlanWriteTime();
                run.StartStep(index);
                var exitCode = await RunStepAsync(run, executable, task);
                lastExitCode = exitCode;

                if (run.Cancellation.IsCancellationRequested)
                {
                    run.EndStep(index, RunState.Cancelled, exitCode);
                    finalState = RunState.Cancelled;
                    break;
                }

                if (exitCode != 0)
                {
                    run.EndStep(index, RunState.Failed, exitCode);
                    finalState = RunState.Failed;
                    break;
                }

                run.EndStep(index, RunState.Succeeded, exitCode);
                RecordPlanLifecycle(run, task, planWriteTimeBefore);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} crashed", run.Id);
            run.Append(OutputStreams.System, $"TF Studio error: {ex.Message}\n");
            finalState = RunState.Failed;
        }
        finally
        {
            run.Complete(finalState, lastExitCode);
            lock (_lock)
            {
                if (_active == run)
                {
                    _active = null;
                }
            }
        }
    }

    private async Task<int> RunStepAsync(Run run, string executable, string task)
    {
        var startInfo = ChildProcess.CreateStartInfo(executable, options.ProjectRoot, redirectInput: true, forceColor: true);
        startInfo.ArgumentList.Add(task);
        foreach (var (key, value) in run.Vars)
        {
            startInfo.ArgumentList.Add($"{key}={value}");
        }

        var display = string.Join(' ', startInfo.ArgumentList.Prepend("task"));
        run.Append(OutputStreams.System, $"$ {display}\n");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            run.Append(OutputStreams.System, $"Could not start task: {ex.Message}\n");
            return -1;
        }

        run.AttachProcess(process);
        try
        {
            var stdout = PumpAsync(process.StandardOutput, run, OutputStreams.Stdout);
            var stderr = PumpAsync(process.StandardError, run, OutputStreams.Stderr);
            try
            {
                await process.WaitForExitAsync(run.Cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                ChildProcess.KillTree(process);
                await process.WaitForExitAsync(CancellationToken.None);
                run.Append(OutputStreams.System,
                    "\nCancelled: the process tree was killed. If Terraform held a state lock, release it with " +
                    $"{TaskNames.Unlock} ID=<lock-id> (the lock ID is printed in the next error).\n");
            }

            // A grandchild that inherited the pipes could keep them open after exit; don't hang on it.
            await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(TimeSpan.FromSeconds(5)));
            return process.ExitCode;
        }
        finally
        {
            run.DetachProcess();
        }
    }

    private static async Task PumpAsync(StreamReader reader, Run run, string stream)
    {
        // Chunked, not line-based: prompts like "Enter a value: " have no trailing newline.
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            run.Append(stream, new string(buffer, 0, read));
        }
    }

    private void RecordPlanLifecycle(Run run, string task, DateTime? planWriteTimeBefore)
    {
        lock (_lock)
        {
            if (task == TaskNames.Plan && PlanWriteTime() is { } written)
            {
                _lastPlan = new PlanStamp(run.Id, run.Workspace, written);
            }
            else if (task == TaskNames.ApplySavedPlan && planWriteTimeBefore is { } applied)
            {
                _lastAppliedPlanWriteTime = applied;
            }
        }
    }

    private DateTime? PlanWriteTime()
    {
        var path = Path.Combine(options.ProjectRoot, ProjectLayout.PlanFile);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    private void TrimHistory()
    {
        while (_runs.Count > MaxHistory && _runs.FindIndex(r => r.IsFinished) is var index and >= 0)
        {
            _runs.RemoveAt(index);
        }
    }

    private static Dictionary<string, string> ValidateVars(Dictionary<string, string>? vars)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in vars ?? [])
        {
            // Strict allow-lists: values end up as `task KEY=value` arguments.
            if (!VarNameRegex().IsMatch(key))
            {
                throw new StudioException($"Invalid variable name '{key}' (expected UPPER_SNAKE_CASE).");
            }

            if (!VarValueRegex().IsMatch(value))
            {
                throw new StudioException($"Invalid value for {key}: letters, digits and . _ - : / @ + , = only.");
            }

            result[key] = value;
        }

        return result;
    }

    private static string NewRunId() =>
        $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(2))}";

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$")]
    private static partial Regex VarNameRegex();

    [GeneratedRegex("^[A-Za-z0-9._:/@+,=-]{1,256}$")]
    private static partial Regex VarValueRegex();
}


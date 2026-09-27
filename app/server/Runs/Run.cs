using System.Diagnostics;
using TfStudio.Server.Api;

namespace TfStudio.Server.Runs;

public static class OutputStreams
{
    public const string Stdout = "stdout";
    public const string Stderr = "stderr";
    public const string System = "system";
    public const string Stdin = "stdin";
}

/// <summary>
/// One execution of one or more Task steps, run sequentially. Holds an append-only event log
/// (output chunks + status changes) that SSE readers replay from any sequence number, then follow live.
/// </summary>
public sealed class Run
{
    private const int MaxEvents = 50_000;
    private const long MaxChars = 8_000_000;

    private readonly Lock _lock = new();
    private readonly List<RunEvent> _events = [];
    private readonly RunStep[] _steps;
    private long _nextSeq = 1;
    private long _chars;
    private TaskCompletionSource _changed = NewSignal();
    private Process? _process;

    public Run(string id, IReadOnlyList<string> tasks, Dictionary<string, string> vars, string? workspace)
    {
        Id = id;
        Vars = vars;
        Workspace = workspace;
        StartedAt = DateTimeOffset.UtcNow;
        Tasks = [.. tasks];
        _steps = [.. tasks.Select(task => new RunStep(task))];
    }

    public string Id { get; }
    public Dictionary<string, string> Vars { get; }
    public string? Workspace { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; private set; }
    public RunState State { get; private set; } = RunState.Running;
    public int? ExitCode { get; private set; }
    public IReadOnlyList<string> Tasks { get; }
    public bool IsFinished => State != RunState.Running;

    internal CancellationTokenSource Cancellation { get; } = new();

    internal Process? CurrentProcess
    {
        get { lock (_lock) { return _process; } }
    }

    internal void AttachProcess(Process process)
    {
        lock (_lock)
        {
            _process = process;
        }

        PublishStatus();
    }

    internal void DetachProcess()
    {
        lock (_lock)
        {
            _process = null;
        }

        PublishStatus();
    }

    internal void Append(string stream, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            _chars += text.Length;
            AddEvent(new RunEvent(_nextSeq, "output", stream, text));
        }
    }

    internal void StartStep(int index)
    {
        lock (_lock)
        {
            _steps[index].State = RunState.Running;
            _steps[index].StartedAt = DateTimeOffset.UtcNow;
        }

        PublishStatus();
    }

    internal void EndStep(int index, RunState state, int? exitCode)
    {
        lock (_lock)
        {
            _steps[index].State = state;
            _steps[index].ExitCode = exitCode;
            _steps[index].EndedAt = DateTimeOffset.UtcNow;
        }

        PublishStatus();
    }

    internal void Complete(RunState state, int? exitCode)
    {
        lock (_lock)
        {
            foreach (var step in _steps.Where(s => s.State == RunState.Pending))
            {
                step.State = RunState.Skipped;
            }

            State = state;
            ExitCode = exitCode;
            EndedAt = DateTimeOffset.UtcNow;
            _process = null;
        }

        PublishStatus();
    }

    public RunSummary ToSummary()
    {
        lock (_lock)
        {
            return SummaryUnlocked();
        }
    }

    /// <summary>Events with Seq &gt; <paramref name="afterSeq"/>, plus a task that completes on the next append.</summary>
    public RunEventBatch ReadAfter(long afterSeq)
    {
        lock (_lock)
        {
            var firstSeq = _events.Count > 0 ? _events[0].Seq : _nextSeq;
            var trimmed = afterSeq + 1 < firstSeq;
            var start = trimmed ? 0 : (int)(afterSeq + 1 - firstSeq);
            var events = start < _events.Count ? _events.GetRange(start, _events.Count - start) : [];
            return new RunEventBatch(events, trimmed ? firstSeq - 1 : null, IsFinished, _changed.Task);
        }
    }

    private void PublishStatus()
    {
        lock (_lock)
        {
            AddEvent(new RunEvent(_nextSeq, "status", Run: SummaryUnlocked()));
        }
    }

    private void AddEvent(RunEvent runEvent)
    {
        _events.Add(runEvent);
        _nextSeq++;

        // Bounded memory: drop the oldest 10% once over budget. Late readers get a "trimmed" marker.
        if (_events.Count > MaxEvents || _chars > MaxChars)
        {
            var drop = Math.Max(1, _events.Count / 10);
            _chars -= _events.Take(drop).Sum(e => (long)(e.Text?.Length ?? 0));
            _events.RemoveRange(0, drop);
        }

        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    private RunSummary SummaryUnlocked() => new(
        Id,
        _steps.Select(s => new RunStepSummary(s.Task, s.State, s.ExitCode, s.StartedAt, s.EndedAt)).ToList(),
        Vars,
        Workspace,
        State,
        ExitCode,
        StartedAt,
        EndedAt,
        AcceptsInput: _process is not null && State == RunState.Running);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RunStep(string task)
    {
        public string Task { get; } = task;
        public RunState State { get; set; } = RunState.Pending;
        public int? ExitCode { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
    }
}

/// <summary>Result of <see cref="Run.ReadAfter"/>.</summary>
/// <param name="Events">New events, oldest first.</param>
/// <param name="TrimmedBefore">Set when events the reader asked for were dropped; value = last dropped seq.</param>
/// <param name="IsFinished">The run has completed (no events will follow the final status event).</param>
/// <param name="Changed">Completes when the next event is appended.</param>
public sealed record RunEventBatch(IReadOnlyList<RunEvent> Events, long? TrimmedBefore, bool IsFinished, Task Changed);

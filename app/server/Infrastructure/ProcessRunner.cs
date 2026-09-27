using System.Diagnostics;

namespace TfStudio.Server.Infrastructure;

/// <summary>Runs short, non-interactive commands (task --list-all, tool --version) to completion.</summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    public static async Task<Result> RunAsync(
        string executable, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = ChildProcess.CreateStartInfo(executable, workingDirectory, redirectInput: false, forceColor: false);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            ChildProcess.KillTree(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"'{Path.GetFileName(executable)}' did not finish within {timeout.TotalSeconds:0}s.");
        }

        return new Result(process.ExitCode, await stdout, await stderr);
    }
}

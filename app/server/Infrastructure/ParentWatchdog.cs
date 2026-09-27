using System.Diagnostics;

namespace TfStudio.Server.Infrastructure;

/// <summary>Stops the server when the Electron process that launched it goes away (crash, kill -9, ...).</summary>
public static class ParentWatchdog
{
    public static async Task WatchAsync(int parentPid, IHostApplicationLifetime lifetime)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            await parent.WaitForExitAsync(lifetime.ApplicationStopping);
        }
        catch (ArgumentException)
        {
            // Parent already gone.
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lifetime.StopApplication();
    }
}

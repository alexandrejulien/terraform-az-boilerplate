using System.Diagnostics;
using System.Text;

namespace TfStudio.Server.Infrastructure;

public static class ChildProcess
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Start info for task/terraform/tool processes: no shell, arguments go through ArgumentList
    /// (never string-concatenated), UTF-8 pipes.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory, bool redirectInput, bool forceColor)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        if (redirectInput)
        {
            startInfo.StandardInputEncoding = Utf8;
        }

        // .env is the single source of truth for the workspace. An inherited TF_WORKSPACE would win
        // over Task's dotenv and silently target a different environment than the one the UI shows.
        startInfo.Environment.Remove("TF_WORKSPACE");
        startInfo.Environment.Remove(StudioOptions.TokenEnvironmentVariable);
        if (forceColor)
        {
            // Pipes aren't TTYs; ask Task for colored output anyway (the UI renders ANSI).
            startInfo.Environment["FORCE_COLOR"] = "1";
        }

        return startInfo;
    }

    public static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied on a grandchild that is already exiting; nothing more we can do.
        }
    }
}

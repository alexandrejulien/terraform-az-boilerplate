using System.Globalization;
using System.Security.Cryptography;

namespace TfStudio.Server.Infrastructure;

/// <summary>
/// Startup options. Command-line: <c>--root &lt;path&gt;</c>, <c>--port &lt;n&gt;</c> (0 = dynamic),
/// <c>--parent-pid &lt;pid&gt;</c>, <c>--task-bin &lt;path&gt;</c>. The API token comes from the
/// <c>TFSTUDIO_TOKEN</c> environment variable (never argv, which other processes can read).
/// </summary>
public sealed class StudioOptions
{
    public const string TokenEnvironmentVariable = "TFSTUDIO_TOKEN";
    public const int DefaultPort = 5080;

    public required string ProjectRoot { get; init; }
    public required string Token { get; init; }
    public required int Port { get; init; }
    public int? ParentPid { get; init; }
    public required string TaskExecutable { get; init; }

    public static StudioOptions FromConfiguration(IConfiguration config)
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        // Child processes (task, terraform, providers) inherit our environment: don't hand them the token.
        Environment.SetEnvironmentVariable(TokenEnvironmentVariable, null);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        }

        return new StudioOptions
        {
            ProjectRoot = ResolveProjectRoot(config["root"]),
            Token = token,
            Port = ParseInt(config["port"], "port") ?? DefaultPort,
            ParentPid = ParseInt(config["parent-pid"], "parent-pid"),
            TaskExecutable = config["task-bin"] is { Length: > 0 } taskBin ? taskBin : "task",
        };
    }

    /// <summary>A project root is a folder laid out like this boilerplate: a Taskfile plus environments/.</summary>
    public static bool IsProjectRoot(string path) =>
        File.Exists(Path.Combine(path, "Taskfile.yml")) && Directory.Exists(Path.Combine(path, "environments"));

    private static string ResolveProjectRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var full = Path.GetFullPath(explicitRoot);
            return IsProjectRoot(full)
                ? full
                : throw new StudioException(
                    $"'{full}' is not a Terraform boilerplate project (expected Taskfile.yml and environments/).");
        }

        // No --root: walk up from the working directory, then from the binary (dev: app/server/bin/...).
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (IsProjectRoot(dir.FullName))
                {
                    return dir.FullName;
                }
            }
        }

        throw new StudioException(
            "No Terraform boilerplate project found. Pass --root <path> (folder containing Taskfile.yml and environments/).");
    }

    private static int? ParseInt(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new StudioException($"--{name} must be a non-negative integer, got '{value}'.");
    }
}

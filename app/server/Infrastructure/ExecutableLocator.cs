namespace TfStudio.Server.Infrastructure;

/// <summary>Resolves a command name against PATH (and PATHEXT on Windows), like a shell would.</summary>
public static class ExecutableLocator
{
    public static string? Find(string name)
    {
        if (Path.IsPathFullyQualified(name))
        {
            return File.Exists(name) ? name : null;
        }

        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [""];
        if (Path.HasExtension(name))
        {
            extensions = ["", .. extensions];
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

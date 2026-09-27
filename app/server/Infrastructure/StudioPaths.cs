namespace TfStudio.Server.Infrastructure;

public static class StudioPaths
{
    /// <summary>
    /// Published (AOT) builds ship wwwroot next to the executable. In dev (dotnet run/build) wwwroot
    /// stays in the project folder, so walk up from bin/&lt;config&gt;/&lt;tfm&gt;/ to the .csproj and serve
    /// the files from there — edits to the UI then only need a browser reload.
    /// </summary>
    public static string FindContentRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDirectory, "wwwroot")))
        {
            return baseDirectory;
        }

        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TfStudio.Server.csproj")))
            {
                return dir.FullName;
            }
        }

        return baseDirectory;
    }
}

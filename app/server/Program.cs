using System.Net;
using TfStudio.Server.Api;
using TfStudio.Server.Infrastructure;
using TfStudio.Server.Plan;
using TfStudio.Server.Project;
using TfStudio.Server.Runs;

var contentRoot = StudioPaths.FindContentRoot();
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot"),
});

StudioOptions options;
try
{
    options = StudioOptions.FromConfiguration(builder.Configuration);
}
catch (StudioException ex)
{
    // Electron surfaces this line in its startup error dialog.
    Console.Error.WriteLine($"TFSTUDIO_ERROR {ex.Message}");
    return 2;
}

builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
// Loopback only: this server can run `terraform apply`, it must never be reachable from the network.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, options.Port));

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<TaskCatalog>();
builder.Services.AddSingleton<ToolsService>();
builder.Services.AddSingleton<RunManager>();
builder.Services.AddSingleton<PlanService>();

var app = builder.Build();

app.UseSecurityHeaders();
app.UseApiToken(options.Token);
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});
app.MapStudioApi();

app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RunManager>().KillActive());

await app.StartAsync();

// With --port 0 Kestrel picks a free port; app.Urls holds the bound address once started.
var address = app.Urls.First();

// Electron waits for this exact line to learn the (dynamic) port.
Console.WriteLine($"TFSTUDIO_READY {address}");
if (options.ParentPid is { } parentPid)
{
    _ = ParentWatchdog.WatchAsync(parentPid, app.Lifetime);
}
else
{
    Console.WriteLine($"TF Studio is serving {options.ProjectRoot}");
    Console.WriteLine($"Open {address}/#token={options.Token}");
}

await app.WaitForShutdownAsync();
return 0;

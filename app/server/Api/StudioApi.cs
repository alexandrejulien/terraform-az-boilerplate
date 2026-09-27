using System.Net.ServerSentEvents;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.HttpResults;
using TfStudio.Server.Infrastructure;
using TfStudio.Server.Plan;
using TfStudio.Server.Project;
using TfStudio.Server.Runs;

namespace TfStudio.Server.Api;

public static class StudioApi
{
    private static readonly string Version =
        typeof(StudioApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static void MapStudioApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // StudioException carries a user-facing message; map it to a status code in one place.
        api.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (StudioException ex)
            {
                var status = ex.Kind switch
                {
                    StudioErrorKind.NotFound => StatusCodes.Status404NotFound,
                    StudioErrorKind.Conflict => StatusCodes.Status409Conflict,
                    StudioErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
                    _ => StatusCodes.Status400BadRequest,
                };
                return TypedResults.Json(new ApiError(ex.Message), AppJsonContext.Default.ApiError, statusCode: status);
            }
        });

        api.MapGet("/health", (StudioOptions options) =>
            TypedResults.Ok(new HealthResponse("ok", Version, options.ProjectRoot)));

        api.MapGet("/project", (ProjectService project) => TypedResults.Ok(project.GetInfo()));

        api.MapGet("/environments/{name}", Results<Ok<EnvironmentFiles>, NotFound<ApiError>> (string name, ProjectService project) =>
            project.ReadEnvironmentFiles(name) is { } files
                ? TypedResults.Ok(files)
                : TypedResults.NotFound(new ApiError($"Environment '{name}' not found.")));

        api.MapGet("/tasks", async (TaskCatalog catalog, CancellationToken ct) =>
            TypedResults.Ok(await catalog.GetTasksAsync(ct)));

        api.MapGet("/tools", async (bool? refresh, ToolsService tools, CancellationToken ct) =>
            TypedResults.Ok(await tools.CheckAsync(refresh ?? false, ct)));

        api.MapGet("/runs", (RunManager runs) => TypedResults.Ok(runs.List()));

        api.MapPost("/runs", async (RunRequest request, RunManager runs, CancellationToken ct) =>
        {
            var run = await runs.StartAsync(request, ct);
            return TypedResults.Created($"/api/runs/{run.Id}", run.ToSummary());
        });

        api.MapGet("/runs/{id}", Results<Ok<RunSummary>, NotFound<ApiError>> (string id, RunManager runs) =>
            runs.Get(id) is { } run
                ? TypedResults.Ok(run.ToSummary())
                : TypedResults.NotFound(new ApiError($"Run '{id}' not found.")));

        api.MapGet("/runs/{id}/events", Results<ServerSentEventsResult<RunEvent>, NotFound<ApiError>> (
            string id, long? after, RunManager runs, CancellationToken ct) =>
            runs.Get(id) is { } run
                ? TypedResults.ServerSentEvents(StreamEvents(run, after ?? 0, ct))
                : TypedResults.NotFound(new ApiError($"Run '{id}' not found.")));

        api.MapPost("/runs/{id}/input", async (string id, InputRequest request, RunManager runs) =>
        {
            await runs.SendInputAsync(id, request.Text);
            return TypedResults.NoContent();
        });

        api.MapPost("/runs/{id}/cancel", (string id, RunManager runs) =>
        {
            runs.Cancel(id);
            return TypedResults.Accepted($"/api/runs/{id}");
        });

        api.MapGet("/plan", async Task<Results<Ok<PlanReport>, NotFound<ApiError>>> (PlanService plans, CancellationToken ct) =>
            await plans.GetReportAsync(ct) is { } report
                ? TypedResults.Ok(report)
                : TypedResults.NotFound(new ApiError($"No {ProjectLayout.PlanJsonFile} yet. Run {TaskNames.Plan}.")));
    }

    /// <summary>Replays the run's log after <paramref name="afterSeq"/>, then follows it live until the run ends.</summary>
    private static async IAsyncEnumerable<SseItem<RunEvent>> StreamEvents(
        Run run, long afterSeq, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = afterSeq;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = run.ReadAfter(cursor);
            if (batch.TrimmedBefore is { } trimmed)
            {
                yield return Item(new RunEvent(trimmed, "trimmed", Text: "Earlier output was trimmed to bound memory usage."));
            }

            foreach (var runEvent in batch.Events)
            {
                cursor = runEvent.Seq;
                yield return Item(runEvent);
            }

            if (batch.IsFinished && batch.Events.Count == 0)
            {
                yield break;
            }

            if (!batch.IsFinished)
            {
                try
                {
                    await batch.Changed.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
            }
        }
    }

    private static SseItem<RunEvent> Item(RunEvent runEvent) =>
        new(runEvent, runEvent.Type) { EventId = runEvent.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture) };
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TfStudio.Server.Api;

/// <summary>
/// Source-generated serializers for everything the API reads or writes (Native AOT has no
/// reflection-based JSON). Add every new request/response type here.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunRequest))]
[JsonSerializable(typeof(InputRequest))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProjectInfo))]
[JsonSerializable(typeof(EnvironmentFiles))]
[JsonSerializable(typeof(IReadOnlyList<TaskInfo>))]
[JsonSerializable(typeof(IReadOnlyList<ToolStatus>))]
[JsonSerializable(typeof(IReadOnlyList<RunSummary>))]
[JsonSerializable(typeof(RunSummary))]
[JsonSerializable(typeof(RunEvent))]
[JsonSerializable(typeof(PlanReport))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

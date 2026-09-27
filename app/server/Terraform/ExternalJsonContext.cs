using System.Text.Json.Serialization;

namespace TfStudio.Server.Terraform;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip)]
[JsonSerializable(typeof(TaskListJson))]
[JsonSerializable(typeof(TfPlanJson))]
[JsonSerializable(typeof(TfBackendStateJson))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class ExternalJsonContext : JsonSerializerContext;

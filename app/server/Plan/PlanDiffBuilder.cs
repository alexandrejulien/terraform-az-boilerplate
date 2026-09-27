using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TfStudio.Server.Api;
using TfStudio.Server.Terraform;

namespace TfStudio.Server.Plan;

/// <summary>
/// Turns a Terraform change (before/after trees plus their after_unknown / *_sensitive mirrors) into
/// a flat list of attribute rows, the way `terraform plan` prints them:
/// <c>tags.Environment: "nonprod" → "prod"</c>, <c>id: (known after apply)</c>, <c>(sensitive value)</c>.
/// Sensitive values never leave the server.
/// </summary>
public static partial class PlanDiffBuilder
{
    public const string KnownAfterApply = "(known after apply)";
    public const string SensitiveValue = "(sensitive value)";
    private const int MaxValueLength = 20_000;

    public static List<AttributeDiff> Build(TfChangeJson change)
    {
        var replacePaths = ReadReplacePaths(change.ReplacePaths);
        var rows = new List<AttributeDiff>();
        Walk(new Node(change.Before, change.After, change.AfterUnknown, change.BeforeSensitive, change.AfterSensitive),
            path: "", Flags.None, rows, replacePaths);
        return rows;
    }

    /// <summary>Single-value view for output_changes, where the unknown/sensitive markers are plain booleans.</summary>
    public static OutputChangeView BuildOutput(string name, string action, TfChangeJson change)
    {
        var unknown = IsTrue(change.AfterUnknown);
        var beforeSensitive = IsTrue(change.BeforeSensitive);
        var afterSensitive = IsTrue(change.AfterSensitive);
        return new OutputChangeView(
            name,
            action,
            beforeSensitive && IsPresent(change.Before) ? SensitiveValue : Render(change.Before),
            unknown ? KnownAfterApply : afterSensitive && IsPresent(change.After) ? SensitiveValue : Render(change.After),
            beforeSensitive || afterSensitive,
            unknown);
    }

    private readonly record struct Node(JsonElement Before, JsonElement After, JsonElement Unknown, JsonElement BeforeSensitive, JsonElement AfterSensitive)
    {
        public Node Child(string key) => new(Prop(Before, key), Prop(After, key), Prop(Unknown, key), Prop(BeforeSensitive, key), Prop(AfterSensitive, key));

        public Node Child(int index) => new(Item(Before, index), Item(After, index), Item(Unknown, index), Item(BeforeSensitive, index), Item(AfterSensitive, index));
    }

    [Flags]
    private enum Flags
    {
        None = 0,
        Unknown = 1,
        BeforeSensitive = 2,
        AfterSensitive = 4,
    }

    private static void Walk(Node node, string path, Flags inherited, List<AttributeDiff> rows, List<string> replacePaths)
    {
        var flags = inherited;
        if (IsTrue(node.Unknown)) flags |= Flags.Unknown;
        if (IsTrue(node.BeforeSensitive)) flags |= Flags.BeforeSensitive;
        if (IsTrue(node.AfterSensitive)) flags |= Flags.AfterSensitive;

        var beforeIsContainer = IsContainer(node.Before);
        var afterIsContainer = IsContainer(node.After);
        var anyContainer = beforeIsContainer || afterIsContainer || IsContainer(node.Unknown);
        var sensitive = (flags & (Flags.BeforeSensitive | Flags.AfterSensitive)) != 0;
        var typeChanged = IsPresent(node.Before) && IsPresent(node.After) && beforeIsContainer != afterIsContainer;

        // Leaf, or a subtree terraform shows as a single value (fully unknown, sensitive, or changed type).
        if (!anyContainer || (flags.HasFlag(Flags.Unknown) && !afterIsContainer) || sensitive || typeChanged)
        {
            if (path.Length > 0)
            {
                AddRow(node, path, flags, rows, replacePaths);
            }

            return;
        }

        var countBefore = rows.Count;
        if (node.Before.ValueKind == JsonValueKind.Object || node.After.ValueKind == JsonValueKind.Object || node.Unknown.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in UnionKeys(node.After, node.Before, node.Unknown))
            {
                Walk(node.Child(key), JoinKey(path, key), flags, rows, replacePaths);
            }
        }
        else
        {
            var length = Math.Max(Length(node.Before), Math.Max(Length(node.After), Length(node.Unknown)));
            for (var i = 0; i < length; i++)
            {
                Walk(node.Child(i), $"{path}[{i}]", flags, rows, replacePaths);
            }
        }

        // Empty map/list on both sides (e.g. `tags = {}`) is still worth a row.
        if (rows.Count == countBefore && path.Length > 0)
        {
            AddRow(node, path, flags, rows, replacePaths);
        }
    }

    private static void AddRow(Node node, string path, Flags flags, List<AttributeDiff> rows, List<string> replacePaths)
    {
        var unknown = flags.HasFlag(Flags.Unknown);
        if (!unknown && !IsPresent(node.Before) && !IsPresent(node.After))
        {
            return; // null → null: terraform hides these too.
        }

        var beforeSensitive = flags.HasFlag(Flags.BeforeSensitive);
        var afterSensitive = flags.HasFlag(Flags.AfterSensitive);
        rows.Add(new AttributeDiff(
            path,
            beforeSensitive && IsPresent(node.Before) ? SensitiveValue : Render(node.Before),
            unknown ? KnownAfterApply : afterSensitive && IsPresent(node.After) ? SensitiveValue : Render(node.After),
            Changed: unknown || !ValuesEqual(node.Before, node.After),
            unknown,
            beforeSensitive || afterSensitive,
            ForcesReplacement(path, replacePaths)));
    }

    private static string? Render(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => $"\"{value.GetString()}\"",
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Serialize(value, ExternalJsonContext.Default.JsonElement),
            _ => value.GetRawText(),
        };
        return text is { Length: > MaxValueLength } ? text[..MaxValueLength] + " …(truncated)" : text;
    }

    private static bool ValuesEqual(JsonElement a, JsonElement b)
    {
        // Terraform treats null and absent as the same thing.
        var aPresent = IsPresent(a);
        var bPresent = IsPresent(b);
        return aPresent == bPresent && (!aPresent || JsonElement.DeepEquals(a, b));
    }

    /// <summary>replace_paths is a list of attribute paths, each a list of keys (string) and indexes (number).</summary>
    private static List<string> ReadReplacePaths(JsonElement replacePaths)
    {
        var result = new List<string>();
        if (replacePaths.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var segments in replacePaths.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Array))
        {
            var path = "";
            foreach (var segment in segments.EnumerateArray())
            {
                path = segment.ValueKind == JsonValueKind.Number
                    ? $"{path}[{segment.GetRawText()}]"
                    : JoinKey(path, segment.ToString());
            }

            if (path.Length > 0)
            {
                result.Add(path);
            }
        }

        return result;
    }

    private static bool ForcesReplacement(string path, List<string> replacePaths) =>
        replacePaths.Any(rp => IsSameOrDescendant(path, rp) || IsSameOrDescendant(rp, path));

    private static bool IsSameOrDescendant(string path, string ancestor) =>
        path == ancestor
        || (path.Length > ancestor.Length
            && path.StartsWith(ancestor, StringComparison.Ordinal)
            && path[ancestor.Length] is '.' or '[');

    private static string JoinKey(string path, string key)
    {
        if (IdentifierRegex().IsMatch(key))
        {
            return path.Length == 0 ? key : $"{path}.{key}";
        }

        var escaped = new StringBuilder(key.Length + 4).Append("[\"").Append(key.Replace("\"", "\\\"")).Append("\"]").ToString();
        return path + escaped;
    }

    private static IEnumerable<string> UnionKeys(params JsonElement[] objects)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in objects.Where(o => o.ValueKind == JsonValueKind.Object))
        {
            foreach (var property in obj.EnumerateObject())
            {
                if (seen.Add(property.Name))
                {
                    yield return property.Name;
                }
            }
        }
    }

    private static JsonElement Prop(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) ? value : default;

    private static JsonElement Item(JsonElement element, int index) =>
        element.ValueKind == JsonValueKind.Array && index < element.GetArrayLength() ? element[index] : default;

    private static int Length(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : 0;

    private static bool IsContainer(JsonElement element) =>
        element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    private static bool IsPresent(JsonElement element) =>
        element.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static bool IsTrue(JsonElement element) => element.ValueKind == JsonValueKind.True;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial Regex IdentifierRegex();
}

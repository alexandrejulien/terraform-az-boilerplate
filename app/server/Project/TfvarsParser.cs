using System.Text.RegularExpressions;

namespace TfStudio.Server.Project;

/// <summary>Reads top-level <c>key = value</c> assignments from a .tfvars file. Not a full HCL parser.</summary>
public static partial class TfvarsParser
{
    public static Dictionary<string, string> Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in AssignmentRegex().Matches(content))
        {
            values[match.Groups["key"].Value] = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["bare"].Value.Trim();
        }

        return values;
    }

    [GeneratedRegex("""^[ \t]*(?<key>[A-Za-z_][A-Za-z0-9_-]*)[ \t]*=[ \t]*(?:"(?<quoted>(?:[^"\\\r\n]|\\.)*)"|(?<bare>[^"#\r\n{\[][^#\r\n]*))""", RegexOptions.Multiline)]
    private static partial Regex AssignmentRegex();
}

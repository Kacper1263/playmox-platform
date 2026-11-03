using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlaymoxAPI.Services.Platform;

internal static class GameServerCustomParameters
{
    private static readonly Regex SimpleKeyRegex = new("^[A-Za-z0-9_.-]{1,64}$", RegexOptions.Compiled);

    public static Dictionary<string, string> ParseFromConfigRoot(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("customParameters", out var customParameters) ||
            customParameters.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in customParameters.EnumerateObject())
        {
            var key = property.Name.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText()
            };
        }

        return result;
    }

    public static string BuildIniUpsertScript(string filePath, IReadOnlyDictionary<string, string> customParameters)
        => BuildKeyValueUpsertScript(filePath, customParameters, "=");

    public static string BuildSpaceSeparatedUpsertScript(string filePath, IReadOnlyDictionary<string, string> customParameters)
    {
        if (customParameters.Count == 0)
        {
            return string.Empty;
        }

        var filePathEscaped = EscapeForSingleQuotes(filePath);
        var lines = new StringBuilder();
        lines.AppendLine($"touch '{filePathEscaped}' && chown gameserver:gameserver '{filePathEscaped}' || true");
        foreach (var pair in customParameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryNormalizeKey(pair.Key, out var normalizedKey))
            {
                continue;
            }

            var keyEscaped = EscapeForSingleQuotes(normalizedKey);
            var valueEscaped = EscapeForSingleQuotes(SanitizeValue(pair.Value));
            lines.AppendLine("TMP_FILE=\"$(mktemp)\"");
            lines.AppendLine(
                $"awk -v k='{keyEscaped}' -v v='{valueEscaped}' 'BEGIN{{done=0}} $1==k {{print k \" \" v; done=1; next}} {{print}} END{{if(!done) print k \" \" v}}' '{filePathEscaped}' > \"$TMP_FILE\"");
            lines.AppendLine($"chown gameserver:gameserver \"$TMP_FILE\" 2>/dev/null || true");
            lines.AppendLine($"chmod 644 \"$TMP_FILE\" 2>/dev/null || true");
            lines.AppendLine($"mv \"$TMP_FILE\" '{filePathEscaped}'");
            lines.AppendLine($"chown gameserver:gameserver '{filePathEscaped}' 2>/dev/null || true");
        }

        return lines.ToString();
    }

    public static string BuildFactorioJsonUpsertScript(string filePath, IReadOnlyDictionary<string, string> customParameters)
    {
        if (customParameters.Count == 0)
        {
            return string.Empty;
        }

        var filePathEscaped = EscapeForSingleQuotes(filePath);
        var lines = new StringBuilder();
        lines.AppendLine($"touch '{filePathEscaped}' && chown gameserver:gameserver '{filePathEscaped}' || true");
        lines.AppendLine($"if ! jq empty '{filePathEscaped}' >/dev/null 2>&1; then echo '{{}}' > '{filePathEscaped}'; fi");
        foreach (var pair in customParameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryNormalizeKey(pair.Key, out var normalizedKey))
            {
                continue;
            }

            var keyEscaped = EscapeForSingleQuotes(normalizedKey);
            var valueEscaped = EscapeForSingleQuotes(SanitizeValue(pair.Value));
            lines.AppendLine("TMP_FILE=\"$(mktemp)\"");
            lines.AppendLine(
                $"jq --arg k '{keyEscaped}' --arg v '{valueEscaped}' '.[$k]=$v' '{filePathEscaped}' > \"$TMP_FILE\" && mv \"$TMP_FILE\" '{filePathEscaped}'");
            lines.AppendLine($"chown gameserver:gameserver '{filePathEscaped}' 2>/dev/null || true");
            lines.AppendLine($"chmod 644 '{filePathEscaped}' 2>/dev/null || true");
        }

        return lines.ToString();
    }

    public static string BuildKeyValueUpsertScript(
        string filePath,
        IReadOnlyDictionary<string, string> customParameters,
        string separator)
    {
        if (customParameters.Count == 0)
        {
            return string.Empty;
        }

        var filePathEscaped = EscapeForSingleQuotes(filePath);
        var separatorEscaped = EscapeForSingleQuotes(separator);
        var lines = new StringBuilder();
        lines.AppendLine($"touch '{filePathEscaped}' && chown gameserver:gameserver '{filePathEscaped}' || true");
        foreach (var pair in customParameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryNormalizeKey(pair.Key, out var normalizedKey))
            {
                continue;
            }

            var keyEscaped = EscapeForSingleQuotes(normalizedKey);
            var valueEscaped = EscapeForSingleQuotes(SanitizeValue(pair.Value));
            lines.AppendLine("TMP_FILE=\"$(mktemp)\"");
            lines.AppendLine(
                $"awk -v k='{keyEscaped}' -v v='{valueEscaped}' -v sep='{separatorEscaped}' 'BEGIN{{done=0}} index($0, k sep)==1 {{print k sep v; done=1; next}} {{print}} END{{if(!done) print k sep v}}' '{filePathEscaped}' > \"$TMP_FILE\"");
            lines.AppendLine($"chown gameserver:gameserver \"$TMP_FILE\" 2>/dev/null || true");
            lines.AppendLine($"chmod 644 \"$TMP_FILE\" 2>/dev/null || true");
            lines.AppendLine($"mv \"$TMP_FILE\" '{filePathEscaped}'");
            lines.AppendLine($"chown gameserver:gameserver '{filePathEscaped}' 2>/dev/null || true");
        }

        return lines.ToString();
    }

    private static bool TryNormalizeKey(string? raw, out string key)
    {
        key = (raw ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(key) && SimpleKeyRegex.IsMatch(key);
    }

    private static string SanitizeValue(string? value)
        => (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static string EscapeForSingleQuotes(string value)
        => value.Replace("'", "'\"'\"'", StringComparison.Ordinal);
}

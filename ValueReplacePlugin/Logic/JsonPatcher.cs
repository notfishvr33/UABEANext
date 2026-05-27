using Newtonsoft.Json.Linq;
using ValueReplacePlugin.Config;

namespace ValueReplacePlugin.Logic;

public static class JsonPatcher
{
    public static void ApplyPatch(JObject target, ReplaceRule rule)
    {
        if (rule.PathId.HasValue)
            target["m_PathID"] = rule.PathId.Value;

        if (rule.FileId.HasValue)
            target["m_FileID"] = rule.FileId.Value;

        foreach (var entry in rule.Fields)
        {
            var value = ParseValue(entry.Value);
            SetNestedValue(target, entry.Field, value);
        }
    }

    private static void SetNestedValue(JObject root, string fieldPath, JToken value)
    {
        var parts = fieldPath.Split('.');
        JObject current = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (current[part] is not JObject child)
            {
                child = new JObject();
                current[part] = child;
            }
            current = child;
        }

        current[parts[^1]] = value;
    }

    private static JToken ParseValue(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return JToken.FromObject(true);

        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            return JToken.FromObject(false);

        if (trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
            return JValue.CreateNull();

        if (long.TryParse(trimmed, out var longVal))
            return JToken.FromObject(longVal);

        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
            return JToken.FromObject(dblVal);

        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            try { return JToken.Parse(trimmed); }
            catch { }
        }

        return JToken.FromObject(raw);
    }
}

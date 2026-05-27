using Newtonsoft.Json.Linq;
using ValueReplacePlugin.Config;

namespace ValueReplacePlugin.Logic;

public static class JsonPatcher
{
    public static void ApplyPatch(JObject target, ReplaceRule rule)
    {
        foreach (var entry in rule.Fields)
        {
            var value = ParseValue(entry.Value);
            SetValue(target, entry.Field, value);
        }
    }

    private static void SetValue(JObject root, string fieldPath, JToken value)
    {
        if (fieldPath.Contains("[first="))
        {
            SetNestedValueWithArrayLookup(root, fieldPath, value);
            return;
        }

        if (!fieldPath.Contains('.'))
        {
            if (TrySetInFirstSecondArray(root, fieldPath, value))
                return;
        }

        SetNestedValue(root, fieldPath, value);
    }

    private static bool TrySetInFirstSecondArray(JObject root, string name, JToken value)
    {
        bool found = false;
        foreach (var array in FindAllArrays(root))
        {
            foreach (var item in array)
            {
                if (item is JObject obj &&
                    obj["first"] is JToken firstToken &&
                    firstToken.Type == JTokenType.String &&
                    (string?)firstToken == name)
                {
                    obj["second"] = value;
                    found = true;
                }
            }
        }
        return found;
    }

    private static IEnumerable<JArray> FindAllArrays(JToken token)
    {
        if (token is JArray arr)
        {
            yield return arr;
            foreach (var child in arr)
                foreach (var nested in FindAllArrays(child))
                    yield return nested;
        }
        else if (token is JObject obj)
        {
            foreach (var prop in obj.Properties())
                foreach (var nested in FindAllArrays(prop.Value))
                    yield return nested;
        }
    }

    private static void SetNestedValueWithArrayLookup(JObject root, string fieldPath, JToken value)
    {
        var bracketIdx = fieldPath.IndexOf("[first=", StringComparison.Ordinal);
        var pathBefore = fieldPath[..bracketIdx].TrimEnd('.');
        var remainder = fieldPath[(bracketIdx + 7)..];

        var closingIdx = remainder.IndexOf(']');
        if (closingIdx < 0)
        {
            SetNestedValue(root, fieldPath, value);
            return;
        }

        var keyName = remainder[..closingIdx];
        var afterBracket = remainder[(closingIdx + 1)..].TrimStart('.');

        JToken? arrayToken = string.IsNullOrEmpty(pathBefore)
            ? root
            : NavigatePath(root, pathBefore);

        if (arrayToken is not JArray array)
            return;

        foreach (var item in array)
        {
            if (item is JObject obj &&
                obj["first"] is JToken firstToken &&
                (string?)firstToken == keyName)
            {
                if (string.IsNullOrEmpty(afterBracket))
                    obj.Replace(value);
                else
                    SetNestedValue(obj, afterBracket, value);
                return;
            }
        }
    }

    private static JToken? NavigatePath(JObject root, string dotPath)
    {
        var parts = dotPath.Split('.');
        JToken current = root;
        foreach (var part in parts)
        {
            if (current is JObject obj && obj[part] is JToken next)
                current = next;
            else
                return null;
        }
        return current;
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

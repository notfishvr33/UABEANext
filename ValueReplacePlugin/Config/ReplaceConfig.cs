using Newtonsoft.Json;

namespace ValueReplacePlugin.Config;

public class ReplaceConfig
{
    [JsonProperty("rules")]
    public List<ReplaceRule> Rules { get; set; } = [];

    public static ReplaceConfig FromJson(string json)
    {
        return JsonConvert.DeserializeObject<ReplaceConfig>(json)
               ?? new ReplaceConfig();
    }

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}

public class ReplaceRule
{
    [JsonProperty("matchName", NullValueHandling = NullValueHandling.Ignore)]
    public string? MatchName { get; set; }

    [JsonProperty("pathId", NullValueHandling = NullValueHandling.Ignore)]
    public long? PathId { get; set; }

    [JsonProperty("fileId", NullValueHandling = NullValueHandling.Ignore)]
    public int? FileId { get; set; }

    [JsonProperty("fields")]
    public List<FieldEntry> Fields { get; set; } = [];
}

public class FieldEntry
{
    [JsonProperty("field")]
    public string Field { get; set; } = string.Empty;

    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;
}

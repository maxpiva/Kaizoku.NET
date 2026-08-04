using System.Resources;
using System.Text.Json.Serialization;

namespace Mihon.ExtensionsBridge.Models;

public record TachiyomiSource
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("lang")]
    public string Language { get; set; }
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("BaseUrl")]
    public string BaseUrl { get; set; }
    [JsonPropertyName("versionId")]
    public int VersionId { get; set; }
    public List<string> MirrorUrls { get; set; } = [];

}
public record TachiyomiSourceV2
{
    public string id { get; set; }
    public string name { get; set; }
    public string language { get; set; }
    public string homeUrl { get; set; }
    public List<string> mirrorUrls { get; set; }
}

public class ResourcesV2
{
    public string apkUrl { get; set; }
    public string iconUrl { get; set; }
    public string jarUrl { get; set; }
}
public class ContactV2
{
    public string website { get; set; }
    public string discord { get; set; }
}

public class ExtensionListV2
{
    public List<TachiyomiExtensionV2> extensions { get; set; }
}

public class Resources
{
    public string apkUrl { get; set; }
    public string iconUrl { get; set; }
    public string jarUrl { get; set; }
}


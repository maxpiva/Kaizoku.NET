using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

/// <summary>
/// Model class to represent an extension
/// </summary>
public class SuwayomiExtension
{
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("pkgName")]
    public string PkgName { get; set; } = string.Empty;
    [JsonPropertyName("versionName")]
    public string VersionName { get; set; } = string.Empty;
    [JsonPropertyName("versionCode")]
    public long VersionCode { get; set; } = 0;
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;
    [JsonPropertyName("apkName")]
    public string ApkName { get; set; } = string.Empty;
    [JsonPropertyName("isNsfw")]
    public bool IsNsfw { get; set; }
    [JsonPropertyName("installed")]
    public bool Installed { get; set; }
    [JsonPropertyName("hasUpdate")]
    public bool HasUpdate { get; set; }
    [JsonPropertyName("iconUrl")]
    public string IconUrl { get; set; } = string.Empty;
    [JsonPropertyName("obsolete")]
    public bool Obsolete { get; set; }

}
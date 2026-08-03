using System.Text.Json.Serialization;

namespace Mihon.ExtensionsBridge.Models;

/// <summary>
/// Root of the new Mihon 0.20+ extension repository index format (protojson <c>index.json</c>).
/// </summary>
public record MihonIndexV2
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("badgeLabel")]
    public string? BadgeLabel { get; set; }

    [JsonPropertyName("signingKey")]
    public string? SigningKey { get; set; }

    [JsonPropertyName("contact")]
    public MihonIndexV2Contact? Contact { get; set; }

    /// <summary>
    /// Inline extension list. This is a protobuf oneof; a repository may instead publish an
    /// <c>extensionListUrl</c> string, in which case this property is null and the format is
    /// treated as unsupported (legacy fallback applies).
    /// </summary>
    [JsonPropertyName("extensionList")]
    public MihonIndexV2ExtensionList? ExtensionList { get; set; }

    [JsonPropertyName("extensionListUrl")]
    public string? ExtensionListUrl { get; set; }
}

public record MihonIndexV2Contact
{
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("discord")]
    public string? Discord { get; set; }
}

public record MihonIndexV2ExtensionList
{
    [JsonPropertyName("extensions")]
    public List<MihonIndexV2Extension>? Extensions { get; set; }
}

public record MihonIndexV2Extension
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("packageName")]
    public string? PackageName { get; set; }

    [JsonPropertyName("resources")]
    public MihonIndexV2Resources? Resources { get; set; }

    [JsonPropertyName("extensionLib")]
    public string? ExtensionLib { get; set; }

    /// <summary>Protojson serializes int64 as a JSON string.</summary>
    [JsonPropertyName("versionCode")]
    public string? VersionCode { get; set; }

    [JsonPropertyName("versionName")]
    public string? VersionName { get; set; }

    /// <summary>
    /// "CONTENT_WARNING_SAFE" | "CONTENT_WARNING_MIXED" | "CONTENT_WARNING_NSFW"
    /// (may be absent for SAFE; short forms "SAFE"/"MIXED"/"NSFW" are also accepted).
    /// </summary>
    [JsonPropertyName("contentWarning")]
    public string? ContentWarning { get; set; }

    [JsonPropertyName("sources")]
    public List<MihonIndexV2Source>? Sources { get; set; }
}

public record MihonIndexV2Resources
{
    [JsonPropertyName("apkUrl")]
    public string? ApkUrl { get; set; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("jarUrl")]
    public string? JarUrl { get; set; }
}

public record MihonIndexV2Source
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("homeUrl")]
    public string? HomeUrl { get; set; }

    [JsonPropertyName("mirrorUrls")]
    public List<string>? MirrorUrls { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

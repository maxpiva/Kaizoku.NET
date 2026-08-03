using Mihon.ExtensionsBridge.Models.Extensions;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Contributions;

public sealed class ContributionRecordV1
{
    public const int SchemaVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; init; } = SchemaVersion;
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("package")]
    public string Package { get; init; } = string.Empty;
    [JsonPropertyName("sourceId")]
    public long SourceId { get; init; }
    [JsonPropertyName("sourceName")]
    public string SourceName { get; init; } = string.Empty;
    [JsonPropertyName("sourceLanguage")]
    public string SourceLanguage { get; init; } = string.Empty;
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
    [JsonPropertyName("realUrl")]
    public string? RealUrl { get; init; }
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; init; }
    [JsonPropertyName("author")]
    public string? Author { get; init; }
    [JsonPropertyName("artist")]
    public string? Artist { get; init; }
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("genre")]
    public string? Genre { get; init; }
    [JsonPropertyName("status")]
    public int Status { get; init; }
    [JsonPropertyName("seenInPopular")]
    public bool SeenInPopular { get; init; }
    [JsonPropertyName("seenInLatest")]
    public bool SeenInLatest { get; init; }

    public static ContributionRecordV1 FromManga(
        string package, long sourceId, string sourceName, string sourceLanguage,
        ParsedManga manga, bool seenInPopular, bool seenInLatest)
    {
        return new ContributionRecordV1
        {
            Id = ContributionIdentity.Create(package, sourceId, manga.Url),
            Package = package,
            SourceId = sourceId,
            SourceName = sourceName,
            SourceLanguage = sourceLanguage,
            Url = manga.Url,
            RealUrl = manga.RealUrl,
            Title = manga.Title,
            ThumbnailUrl = manga.ThumbnailUrl,
            Author = manga.Author,
            Artist = manga.Artist,
            Description = manga.Description,
            Genre = manga.Genre,
            Status = (int)manga.Status,
            SeenInPopular = seenInPopular,
            SeenInLatest = seenInLatest
        };
    }
}

public sealed class ContributionBatchV1
{
    public const int SchemaVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; init; } = SchemaVersion;
    [JsonPropertyName("generatedUtc")]
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
    [JsonPropertyName("records")]
    public List<ContributionRecordV1> Records { get; init; } = [];
}

public static class ContributionIdentity
{
    public static string Create(string package, long sourceId, string mangaUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(mangaUrl);
        string sourceNamespace = package.Trim().ToLowerInvariant() + "\n" +
                                 sourceId.ToString(CultureInfo.InvariantCulture) + "\n" +
                                 mangaUrl.Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceNamespace))).ToLowerInvariant();
    }
}

public sealed class ContributionStatusDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
    [JsonPropertyName("state")]
    public string State { get; init; } = ContributionStates.Idle;
    [JsonPropertyName("lastStartedUtc")]
    public DateTime? LastStartedUtc { get; init; }
    [JsonPropertyName("lastCompletedUtc")]
    public DateTime? LastCompletedUtc { get; init; }
    [JsonPropertyName("itemsCollected")]
    public int ItemsCollected { get; init; }
    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }
}

public static class ContributionStates
{
    public const string Disabled = "disabled";
    public const string Idle = "idle";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Yielding = "yielding";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Contributions.Upload;

/// <summary>
/// The client-owned payload stored inside a contribution's opaque <c>data</c> blob.
/// The worker never inspects it; only Rensaio clients (upload and snapshot download)
/// need to agree on this shape. Never include the contributor UUID or any
/// machine-identifying data here.
/// </summary>
public sealed class ContributionBlobPayloadV1
{
    public const int SchemaVersion = 1;

    [JsonPropertyName("v")]
    public int V { get; init; } = SchemaVersion;
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
    [JsonPropertyName("latestChapter")]
    public string? LatestChapter { get; init; }
    [JsonPropertyName("observedUtc")]
    public DateTime ObservedUtc { get; init; }

    public static ContributionBlobPayloadV1 FromRecord(ContributionRecordV1 record, DateTime observedUtc) => new()
    {
        Id = record.Id,
        Package = record.Package,
        SourceId = record.SourceId,
        SourceName = record.SourceName,
        SourceLanguage = record.SourceLanguage,
        Url = record.Url,
        RealUrl = record.RealUrl,
        Title = record.Title,
        ThumbnailUrl = record.ThumbnailUrl,
        Author = record.Author,
        Artist = record.Artist,
        Description = record.Description,
        Genre = record.Genre,
        LatestChapter = null,
        ObservedUtc = observedUtc
    };
}

/// <summary>
/// Binary envelope for the opaque blob: a one-byte compression marker followed by the
/// compressed UTF-8 JSON payload. Encoding always uses Brotli; decoding accepts both
/// markers so the format can migrate.
/// </summary>
public static class ContributionBlobEnvelope
{
    public const byte BrotliMarker = 0x01;
    public const byte GzipMarker = 0x02;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static byte[] Encode(ContributionBlobPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var output = new MemoryStream();
        output.WriteByte(BrotliMarker);
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(json);
        }
        return output.ToArray();
    }

    public static string EncodeBase64(ContributionBlobPayloadV1 payload)
        => Convert.ToBase64String(Encode(payload));

    public static ContributionBlobPayloadV1 Decode(byte[] envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Length < 2)
            throw new InvalidDataException("Contribution blob envelope is too short.");
        using var input = new MemoryStream(envelope, 1, envelope.Length - 1);
        using Stream decompressor = envelope[0] switch
        {
            BrotliMarker => new BrotliStream(input, CompressionMode.Decompress),
            GzipMarker => new GZipStream(input, CompressionMode.Decompress),
            _ => throw new InvalidDataException($"Unknown contribution blob marker 0x{envelope[0]:x2}.")
        };
        return JsonSerializer.Deserialize<ContributionBlobPayloadV1>(decompressor, JsonOptions)
               ?? throw new InvalidDataException("Contribution blob payload deserialized to null.");
    }

    /// <summary>
    /// Content hash used by the client-side delta store: lowercase-hex SHA-256 of the
    /// canonical (uncompressed) payload JSON with <see cref="ContributionBlobPayloadV1.ObservedUtc"/>
    /// normalized out. ObservedUtc tracks the collection file's generation time and changes
    /// every run; hashing it would make every record look modified and defeat the delta skip.
    /// </summary>
    public static string PayloadHash(ContributionBlobPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ContributionBlobPayloadV1 canonical = new()
        {
            Id = payload.Id,
            Package = payload.Package,
            SourceId = payload.SourceId,
            SourceName = payload.SourceName,
            SourceLanguage = payload.SourceLanguage,
            Url = payload.Url,
            RealUrl = payload.RealUrl,
            Title = payload.Title,
            ThumbnailUrl = payload.ThumbnailUrl,
            Author = payload.Author,
            Artist = payload.Artist,
            Description = payload.Description,
            Genre = payload.Genre,
            LatestChapter = payload.LatestChapter,
            ObservedUtc = default
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }
}

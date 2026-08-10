using RensaioBackend.Services.Contributions.Upload;
using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Contributions.Snapshot;

// --- Wire DTOs for the exported contribution snapshot (titles.json, sources.json, metadata.json) ---

/// <summary>A row from titles.json: the public title identity used to link source rows.</summary>
public sealed class SnapshotTitleRow
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
}

/// <summary>A row from sources.json: an encrypted contribution blob keyed by its MD5 identity.</summary>
public sealed class SnapshotSourceRow
{
    [JsonPropertyName("title_id")]
    public string TitleId { get; init; } = string.Empty;
    /// <summary>Lowercase-hex MD5 identity (32 chars) of the contribution.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    /// <summary>Base64 of the AES-CBC ciphertext, or null when the row carries no blob.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

/// <summary>A row from metadata.json: a title's link to an external metadata provider.</summary>
public sealed class SnapshotMetadataRow
{
    [JsonPropertyName("title_id")]
    public string TitleId { get; init; } = string.Empty;
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;
    [JsonPropertyName("provider_key")]
    public string ProviderKey { get; init; } = string.Empty;
    [JsonPropertyName("type")]
    public int Type { get; init; }
}

// --- On-disk snapshot format (snapshot-v1.json) ---

/// <summary>The decoded snapshot written to disk after a successful download run.</summary>
public sealed class ContributionSnapshotV1
{
    public int Version { get; set; } = 1;
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public List<ContributionSnapshotTitleV1> Titles { get; set; } = [];
    public List<ContributionSnapshotRecordV1> Records { get; set; } = [];
    public List<ContributionSnapshotMetadataV1> Metadata { get; set; } = [];
}

public sealed class ContributionSnapshotTitleV1
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public sealed class ContributionSnapshotRecordV1
{
    /// <summary>Lowercase-hex MD5 identity of the contribution (the source row id).</summary>
    public string Key { get; set; } = string.Empty;
    public string TitleId { get; set; } = string.Empty;
    /// <summary>True when <see cref="TitleId"/> has no matching row in titles.json.</summary>
    public bool TitleIdDangling { get; set; }
    public ContributionBlobPayloadV1 Payload { get; set; } = new();
}

public sealed class ContributionSnapshotMetadataV1
{
    public string TitleId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public int Type { get; set; }
}

// --- Status surface (API + frontend) ---

public static class ContributionSnapshotStates
{
    public const string Disabled = "disabled";
    public const string Idle = "idle";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed class ContributionSnapshotStatusDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
    [JsonPropertyName("state")]
    public string State { get; init; } = ContributionSnapshotStates.Idle;
    [JsonPropertyName("lastStartedUtc")]
    public DateTime? LastStartedUtc { get; init; }
    [JsonPropertyName("lastCompletedUtc")]
    public DateTime? LastCompletedUtc { get; init; }
    [JsonPropertyName("unchanged")]
    public bool Unchanged { get; init; }
    [JsonPropertyName("titles")]
    public int Titles { get; init; }
    [JsonPropertyName("recordsDecoded")]
    public int RecordsDecoded { get; init; }
    [JsonPropertyName("recordsSkipped")]
    public int RecordsSkipped { get; init; }
    [JsonPropertyName("recordsFailed")]
    public int RecordsFailed { get; init; }
    [JsonPropertyName("danglingTitleRefs")]
    public int DanglingTitleRefs { get; init; }
    [JsonPropertyName("metadataLinks")]
    public int MetadataLinks { get; init; }
    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }
}

// --- Client-side call results (expected failures modeled, not thrown) ---

public enum SnapshotFetchStatus
{
    Success,
    /// <summary>304: the file is unchanged since the persisted ETag.</summary>
    NotModified,
    /// <summary>404: the file does not exist at the snapshot URL.</summary>
    NotFound,
    /// <summary>HTTP 5xx or a transport error; the fetch may be retried.</summary>
    RetryableError
}

public sealed class SnapshotFileResult
{
    public SnapshotFetchStatus Status { get; init; }
    public byte[]? Body { get; init; }
    public string? ETag { get; init; }
    public string? Error { get; init; }
}

public sealed class SnapshotKeyResult
{
    public bool Success { get; init; }
    public byte[]? Key { get; init; }
    public byte[]? Iv { get; init; }
    public string? Error { get; init; }
}

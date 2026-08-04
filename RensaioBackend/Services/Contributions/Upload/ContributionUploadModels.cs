using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Contributions.Upload;

// --- Wire DTOs for maxpiva's contribution worker (https://contribution.rensaio.net) ---

public sealed class UploadRequest
{
    [JsonPropertyName("items")]
    public List<UploadItem> Items { get; init; } = [];
}

public sealed class UploadItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = UploadItemTypes.Source;
    [JsonPropertyName("action")]
    public string Action { get; init; } = UploadItemActions.Add;
    [JsonPropertyName("data")]
    public object Data { get; init; } = new();
}

public static class UploadItemTypes
{
    public const string Source = "source";
    public const string Metadata = "metadata";
}

public static class UploadItemActions
{
    public const string Add = "add";
    public const string Remove = "remove";
}

public sealed class SourceItemData
{
    /// <summary>Lowercase-hex MD5 identity from <see cref="ContributionUploadKey"/>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
    /// <summary>Base64 of the <see cref="ContributionBlobEnvelope"/>, or null.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

/// <summary>Shape-only: metadata uploads are deferred (no mapping flow yet).</summary>
public sealed class MetadataItemData
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
    [JsonPropertyName("metadata_provider")]
    public string MetadataProvider { get; init; } = string.Empty;
    [JsonPropertyName("metadata_provider_key")]
    public string MetadataProviderKey { get; init; } = string.Empty;
    [JsonPropertyName("link_type")]
    public int LinkType { get; init; }
}

public sealed class UploadResponse
{
    [JsonPropertyName("processed")]
    public int Processed { get; init; }
    /// <summary>Always 0 server-side; dead field kept for wire compatibility.</summary>
    [JsonPropertyName("skipped")]
    public int Skipped { get; init; }
    [JsonPropertyName("errors")]
    public List<UploadItemError> Errors { get; init; } = [];
}

public sealed class UploadItemError
{
    [JsonPropertyName("index")]
    public int Index { get; init; }
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class ContributorResponse
{
    [JsonPropertyName("active")]
    public bool Active { get; init; }
    [JsonPropertyName("admin")]
    public bool Admin { get; init; }
    [JsonPropertyName("ban_reason")]
    public string? BanReason { get; init; }
}

// --- Client-side call results (expected failures modeled, not thrown) ---

public enum ContributionCallStatus
{
    Success,
    /// <summary>404: the contributor UUID is unknown to the worker.</summary>
    UnknownContributor,
    /// <summary>403: the contributor is banned.</summary>
    Banned,
    /// <summary>HTTP 5xx or a transport error; the whole batch rolled back and may be retried.</summary>
    RetryableError
}

public sealed class ContributorProbeResult
{
    public ContributionCallStatus Status { get; init; }
    public ContributorResponse? Contributor { get; init; }
    public string? Error { get; init; }
}

public sealed class UploadCallResult
{
    public ContributionCallStatus Status { get; init; }
    public UploadResponse? Response { get; init; }
    public string? Error { get; init; }
}

// --- Status surface (API + frontend) ---

public static class ContributionUploadStates
{
    public const string Disabled = "disabled";
    public const string Idle = "idle";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    /// <summary>The worker rejected the configured contributor UUID (unknown/malformed).</summary>
    public const string Invalid = "invalid";
    public const string Banned = "banned";
}

public sealed class ContributionUploadStatusDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
    [JsonPropertyName("state")]
    public string State { get; init; } = ContributionUploadStates.Idle;
    [JsonPropertyName("lastStartedUtc")]
    public DateTime? LastStartedUtc { get; init; }
    [JsonPropertyName("lastCompletedUtc")]
    public DateTime? LastCompletedUtc { get; init; }
    [JsonPropertyName("uploaded")]
    public int Uploaded { get; init; }
    [JsonPropertyName("skipped")]
    public int Skipped { get; init; }
    [JsonPropertyName("failed")]
    public int Failed { get; init; }
    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }
    [JsonPropertyName("contributor")]
    public ContributionContributorDto? Contributor { get; init; }
}

public sealed class ContributionContributorDto
{
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }
    [JsonPropertyName("active")]
    public bool Active { get; init; }
    [JsonPropertyName("admin")]
    public bool Admin { get; init; }
    [JsonPropertyName("banReason")]
    public string? BanReason { get; init; }
    [JsonPropertyName("validatedUtc")]
    public DateTime? ValidatedUtc { get; init; }
    /// <summary>Transport-level failure message when validation could not reach the worker.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

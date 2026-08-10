using Mihon.ExtensionsBridge.Models;
using RensaioBackend.Services.Search.Discovery;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Contributions;

public static class ContributionWorkerJson
{
    public const string LinePrefix = "@RCW@";
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class ContributionWorkerRequest
{
    public string ScratchFolder { get; set; } = string.Empty;
    public Preferences? Preferences { get; set; }
    public DiscoveryWorkerExtension Extension { get; set; } = null!;
    public List<long> SourceIds { get; set; } = [];
    public double SourceTimeoutSeconds { get; set; } = 120;
}

public sealed class ContributionWorkerResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ContributionBatchV1 Batch { get; set; } = new();
}

using RensaioBackend.Models.Dto;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Progressive-delivery hooks for a discovery search. When supplied, the search converts each
/// per-source result batch into ready-to-render DTOs (thumb-cached, relevance-scored) and hands
/// them to <see cref="OnResults"/> as soon as a worker reports them, instead of buffering
/// everything until the sweep completes. <see cref="OnProgress"/> fires per extension as it is
/// prepared ("preparing") or finishes searching ("searching").
/// </summary>
public class DiscoveryStreamCallbacks
{
    public const string StagePreparing = "preparing";
    public const string StageSearching = "searching";

    /// <summary>Called with each freshly converted batch of results (already thumb-populated).</summary>
    public Func<IReadOnlyList<DiscoverySeriesDto>, Task>? OnResults { get; set; }

    /// <summary>Called as (stage, completedExtensions, totalExtensions) whenever one extension settles.</summary>
    public Func<string, int, int, Task>? OnProgress { get; set; }
}

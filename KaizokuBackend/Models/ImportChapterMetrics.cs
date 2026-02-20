using System;
using System.Collections.Generic;

namespace KaizokuBackend.Models;

/// <summary>
/// Encapsulates aggregate chapter/provider counts for import operations.
/// </summary>
public sealed class ImportChapterMetrics
{
    private static readonly IReadOnlyCollection<string> _emptyProviders = Array.Empty<string>();

    private readonly IReadOnlyCollection<string> _providers;

    public static ImportChapterMetrics Empty { get; } = new ImportChapterMetrics(0, _emptyProviders);

    public ImportChapterMetrics(int totalDownloads, IReadOnlyCollection<string> providers)
    {
        TotalDownloads = totalDownloads;
        _providers = providers ?? _emptyProviders;
    }

    public int TotalDownloads { get; }

    public IReadOnlyCollection<string> Providers => _providers;

    public int ProviderCount => _providers.Count;
}

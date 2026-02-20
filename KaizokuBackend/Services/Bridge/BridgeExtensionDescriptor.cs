using System.Collections.ObjectModel;

namespace KaizokuBackend.Services.Bridge;

/// <summary>
/// Represents a Mihon extension surfaced through the bridge along with its sources.
/// </summary>
public sealed class BridgeExtensionDescriptor
{
    public string PackageId { get; init; } = string.Empty;
    public string RepositoryId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int VersionCode { get; init; }
    public bool IsNsfw { get; init; }
    public string ApkName { get; init; } = string.Empty;
    public string? IconPath { get; init; }
    public string? IconHash { get; init; }
    public IReadOnlyList<BridgeSourceDescriptor> Sources { get; init; } = ReadOnlyCollection<BridgeSourceDescriptor>.Empty;
}

/// <summary>
/// Represents a single source provided by a Mihon extension.
/// </summary>
public sealed class BridgeSourceDescriptor
{
    public long SourceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public bool SupportsLatest { get; init; }
    public bool IsConfigurable { get; init; }
    public bool IsHttpSource { get; init; }
    public bool IsParsedHttpSource { get; init; }
}

namespace KaizokuBackend.Services.Bridge;

/// <summary>
/// Represents the resolved bridge metadata for a specific provider/source pair.
/// </summary>
public sealed class BridgeSourceContext
{
    public string PackageId { get; }
    public long SourceId { get; }
    public string SourceName { get; }
    public string Language { get; }
    public string? RepositoryId { get; }

    public BridgeSourceContext(string packageId, long sourceId, string sourceName, string language, string? repositoryId = null)
    {
        PackageId = packageId;
        SourceId = sourceId;
        SourceName = sourceName;
        Language = language;
        RepositoryId = repositoryId;
    }
}

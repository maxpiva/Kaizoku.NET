namespace Mihon.ExtensionsBridge.Models;

/// <summary>
/// Disk artifacts of a shadow-prepared (discovery) extension: the downloaded APK and converted JAR
/// live in <see cref="Folder"/> and <see cref="Entry"/> carries the manifest-derived metadata needed
/// to classload the JAR later — in this process or in a short-lived worker process.
/// </summary>
public class DiscoveryArtifact
{
    public RepositoryEntry Entry { get; set; }
    public string Folder { get; set; }
}

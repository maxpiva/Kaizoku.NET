namespace Mihon.ExtensionsBridge.Models;

public class RepositoryEntry
{
    public string Id => Apk.SHA256;
    public string RepositoryId { get; set; }
    public bool IsLocal { get; set; }
    public string Name { get; set; }
    public string ClassName { get; set; }
    public TachiyomiExtension Extension { get; set; }
    public string DownloadUrl { get; set; }
    public DateTimeOffset DownloadUTC { get; set; }
    public FileHash Apk { get; set; }
    public FileHashVersion Jar { get; set; }
    //public FileHashVersion Dll { get; set; }
    public FileHash Icon { get; set; }
}


namespace Mihon.ExtensionsBridge.Models;

public class RepositoryGroup
{
    public string Name { get; set; }
    public int ActiveEntry { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public List<RepositoryEntry> Entries { get; set; } = [];
}


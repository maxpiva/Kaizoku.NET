namespace Mihon.ExtensionsBridge.Models;

public class TachiyomiRepository
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string WebSite { get; set; }
    public string Fingerprint { get; set; }
    public string Url { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset LastUpdatedUTC { get; set; } = DateTimeOffset.MinValue;
    public List<TachiyomiExtension> Extensions { get; set; } = [];


    public TachiyomiRepository(string url)
    {
        Url = url;
    }
}
public class TachiyomiRepositoryV2
{
        public string name { get; set; }
        public string badgeLabel { get; set; }
        public string signingKey { get; set; }
        public ContactV2 contact { get; set; }
        public ExtensionListV2 extensionList { get; set; }
}



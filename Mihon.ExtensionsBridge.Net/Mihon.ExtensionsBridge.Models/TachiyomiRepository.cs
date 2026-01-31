namespace Mihon.ExtensionsBridge.Models;

public class TachiyomiRepository
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string WebSite { get; set; }
    public string Fingerprint { get; set; }
    public string Url { get; set; }
    public DateTimeOffset LastUpdatedUTC { get; set; } = DateTimeOffset.MinValue;
    public List<TachiyomiExtension> Extensions { get; set; } = [];


    public TachiyomiRepository(string url)
    {
        Url = url;
    }
}


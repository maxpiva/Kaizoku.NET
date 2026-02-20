namespace Mihon.ExtensionsBridge.Models.Extensions
{
    public class SourcePreference
    {
        public long SourceId { get; set; }
        public string Language { get; set; }
        public List<KeyPreference> Preferences { get; set; }
    }
}

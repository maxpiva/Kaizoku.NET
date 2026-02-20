namespace Mihon.ExtensionsBridge.Models.Extensions
{
    public class UniquePreference
    {
        public List<KeyLanguage> Languages { get; set; } = [];
        public Preference Preference { get; set; }
    }

    public class KeyLanguage
    {
        public string Key { get; set; } = "";
        public string Language { get; set; } = "";
    }
}

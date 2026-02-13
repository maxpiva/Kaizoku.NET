namespace KaizokuBackend.Migration.Models
{
    public class SuwayomiProp
    {
        public string key { get; set; } = "";
        public string? title { get; set; }
        public string summary { get; set; } = "";
        public object defaultValue { get; set; } = new();
        public List<string> entries { get; set; } = [];
        public List<string> entryValues { get; set; } = [];
        public string defaultValueType { get; set; } = "";
        public object currentValue { get; set; } = new();
        public bool visible { get; set; }
        public string dialogTitle { get; set; } = "";
        public string dialogMessage { get; set; } = "";
        public string text { get; set; } = "";
    }
}

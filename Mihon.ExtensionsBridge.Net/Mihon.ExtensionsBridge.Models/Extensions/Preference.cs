using System;
using System.Collections.Generic;
using System.Text;

namespace Mihon.ExtensionsBridge.Models.Extensions
{
    public class KeyPreference : Preference
    {
        public string Key { get; set; }
    }
    public class Preference
    {
        public int Index { get; set; }
        public string Type { get; set; } = "";
        public string? Title { get; set; }
        public string Summary { get; set; } = "";
        public string DefaultValue { get; set; }
        public List<string> Entries { get; set; } = [];
        public List<string> EntryValues { get; set; } = [];
        public string DefaultValueType { get; set; } = "";
        public string CurrentValue { get; set; }
        public bool Visible { get; set; }
        public string DialogTitle { get; set; } = "";
        public string DialogMessage { get; set; } = "";
        public string Text { get; set; } = "";
    }
}

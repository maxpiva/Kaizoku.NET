using System.Text.Json.Serialization;
using Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;

namespace Mihon.ExtensionsBridge.IKVMCompiler.Models
{
    public class JsonIKVMVersion : IIKVMVersion
    {
        public string Version { get; set; }
        public string OS { get; set; }
        public string Processor { get; set; }
        [JsonIgnore]
        public string AndroidCompatPath { get; }
        public string ToolsNetVersion { get; set; }
        public string JRENetVersion { get; set; }

    }
}
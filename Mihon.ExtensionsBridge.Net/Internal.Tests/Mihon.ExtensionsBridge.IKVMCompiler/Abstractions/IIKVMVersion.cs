using System.Text.Json.Serialization;

namespace Mihon.ExtensionsBridge.IKVMCompiler.Abstractions
{
    public interface IIKVMVersion
    {
        public string Version { get; }
        public string OS { get; }
        public string Processor { get; }
        [JsonIgnore]
        public string AndroidCompatPath { get; }
        public string ToolsNetVersion { get; }
        public string JRENetVersion { get; }
    }
}
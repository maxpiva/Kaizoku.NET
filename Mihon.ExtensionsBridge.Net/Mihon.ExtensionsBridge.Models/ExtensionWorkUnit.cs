using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Models
{
    public class ExtensionWorkUnit
    {
        public ITemporaryDirectory WorkingFolder { get; set; }
        public RepositoryEntry Entry { get; set; }
    }

}

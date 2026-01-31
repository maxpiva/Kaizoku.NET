using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Models
{

    public class TemporaryDirectory : ITemporaryDirectory
    {
        public string Path { get; }
        public TemporaryDirectory(IWorkingFolderStructure workingFolderStructure)
        {
            Path = System.IO.Path.Combine(workingFolderStructure.TempFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // Ignore exceptions during cleanup
            }
        }
    }
}

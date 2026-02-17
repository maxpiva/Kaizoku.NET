using KaizokuBackend.Models.Database;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace KaizokuBackend.Services.Images.Providers
{
    public class ExtensionsImageProvider : IImageProvider
    {
        private readonly IWorkingFolderStructure _workingFolderStructure;

        public ExtensionsImageProvider(IWorkingFolderStructure workingFolderStructure)
        {
            _workingFolderStructure = workingFolderStructure;
        }

        public bool CanProcess(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith("ext://"))
                return true;
            return false;
        }

        public Task<Stream?> ObtainStreamAsync(EtagCacheEntity cache, CancellationToken token)
        {
            string originalFilename = cache.Url.Substring(6);
            string finalPath = Path.GetFullPath(Path.Combine(_workingFolderStructure.ExtensionsFolder, originalFilename));
            if (File.Exists(finalPath))
                return Task.FromResult((Stream?)File.OpenRead(finalPath));
            return Task.FromResult((Stream?)null);
        }
    }
}

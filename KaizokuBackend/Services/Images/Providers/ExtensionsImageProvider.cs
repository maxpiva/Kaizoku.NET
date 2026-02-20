using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace KaizokuBackend.Services.Images.Providers
{
    public class ExtensionsImageProvider : IImageProvider
    {
        private readonly IWorkingFolderStructure _workingFolderStructure;
        private readonly AppDbContext _db;
        public ExtensionsImageProvider(AppDbContext db, IWorkingFolderStructure workingFolderStructure)
        {
            _workingFolderStructure = workingFolderStructure;
            _db = db;
        }

        public bool CanProcess(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith("ext://"))
                return true;
            return false;
        }

        public async Task<Stream?> ObtainStreamAsync(EtagCacheEntity cache, CancellationToken token)
        {
            string originalFilename = cache.Url.Substring(6);
            string finalPath = Path.GetFullPath(Path.Combine(_workingFolderStructure.ExtensionsFolder, originalFilename));
            if (File.Exists(finalPath))
            {
                Stream stream = File.OpenRead(finalPath);
                if (string.IsNullOrEmpty(cache.Etag))
                {
                    cache.Etag = await UrlImageProvider.ComputeMd5HashFromStreamAsync(stream);
                    stream.Position = 0;
                    await _db.SaveChangesAsync();
                }
                return stream;
            }
            return null;
        }
    }
}

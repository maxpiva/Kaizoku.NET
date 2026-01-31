using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IRepositoryDownloader
    {
        Task<TachiyomiRepository> PopulateExtensionsAsync(TachiyomiRepository repository, CancellationToken cancellationToken = default);
        Task DownloadExtensionAsync(TachiyomiRepository repository, ExtensionWorkUnit workUnit, CancellationToken cancellationToken = default);
    }
}
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Core.Abstractions
{
    public interface IRepositoryDownloader
    {
        Task<TachiyomiRepository> PopulateExtensionsAsync(TachiyomiRepository repository, CancellationToken cancellationToken = default);
        Task DownloadExtensionAsync(TachiyomiRepository repository, ExtensionWorkUnit workUnit, CancellationToken cancellationToken = default);
    }
}
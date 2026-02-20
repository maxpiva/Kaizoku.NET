using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Abstractions
{
    public interface IInternalRepositoryManager : IRepositoryManager
    {
        Task InitializeAsync(CancellationToken token = default);
        (TachiyomiRepository?, TachiyomiExtension?) FindRealRepository(TachiyomiExtension extension);
        (TachiyomiRepository?, TachiyomiExtension?) FindRealRepository(TachiyomiRepository repo, TachiyomiExtension ext);
    }
}
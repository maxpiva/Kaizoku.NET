using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IRepositoryManager
    {
        Task<bool> AddOnlineRepositoryAsync(TachiyomiRepository repository, CancellationToken token = default);
        Task<List<TachiyomiRepository>> ListOnlineRepositoryAsync(CancellationToken token = default);
        Task RefreshAllRepositoriesAsync(CancellationToken token = default);
        Task<bool> RemoveOnlineRespositoryAsync(TachiyomiRepository repository, CancellationToken token = default);
    }

    public interface IInternalRepositoryManager : IRepositoryManager
    {
        Task InitializeAsync(CancellationToken token = default);
        (TachiyomiRepository?, TachiyomiExtension?) FindRealRepository(TachiyomiExtension extension);
        (TachiyomiRepository?, TachiyomiExtension?) FindRealRepository(TachiyomiRepository repo, TachiyomiExtension ext);
    }
}
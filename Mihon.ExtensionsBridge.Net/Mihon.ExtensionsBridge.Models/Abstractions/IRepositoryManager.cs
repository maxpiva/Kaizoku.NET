using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IRepositoryManager
    {
        Task<TachiyomiRepository> AddOnlineRepositoryAsync(TachiyomiRepository repository, CancellationToken token = default);
        List<TachiyomiRepository> ListOnlineRepositories();
        Task RefreshAllRepositoriesAsync(CancellationToken token = default);
        Task<bool> RemoveOnlineRespositoryAsync(TachiyomiRepository repository, CancellationToken token = default);
    }
}
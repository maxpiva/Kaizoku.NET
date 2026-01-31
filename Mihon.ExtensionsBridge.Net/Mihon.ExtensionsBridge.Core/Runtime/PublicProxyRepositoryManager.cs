using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Runtime
{
    public class PublicProxyRepositoryManager : IRepositoryManager
    {
        private readonly IInternalRepositoryManager _internalRepositoryManager;
        public PublicProxyRepositoryManager(IInternalRepositoryManager internalRepositoryManager)
        {
            _internalRepositoryManager = internalRepositoryManager;
        }

        public Task<bool> AddOnlineRepositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            TachiyomiRepository addRepo = repository.Clone();
            return _internalRepositoryManager.AddOnlineRepositoryAsync(addRepo, token);
        }

        public async Task<List<TachiyomiRepository>> ListOnlineRepositoryAsync(CancellationToken token = default)
        {
            List<TachiyomiRepository> repos = await _internalRepositoryManager.ListOnlineRepositoryAsync(token).ConfigureAwait(false);
            return repos.ConvertAll(x => x.Clone());
        }

        public Task RefreshAllRepositoriesAsync(CancellationToken token = default)
        {
            return _internalRepositoryManager.RefreshAllRepositoriesAsync(token);
        }

        public Task<bool> RemoveOnlineRespositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            return _internalRepositoryManager.RemoveOnlineRespositoryAsync(repository, token);
        }
    }
}

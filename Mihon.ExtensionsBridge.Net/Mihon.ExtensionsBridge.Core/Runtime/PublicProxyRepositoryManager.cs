using Mihon.ExtensionsBridge.Core.Abstractions;
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

        public Task<TachiyomiRepository> AddOnlineRepositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            TachiyomiRepository addRepo = repository.Clone();
            return _internalRepositoryManager.AddOnlineRepositoryAsync(addRepo, token);
        }

        public List<TachiyomiRepository> ListOnlineRepositories()
        {
            List<TachiyomiRepository> repos = _internalRepositoryManager.ListOnlineRepositories();
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

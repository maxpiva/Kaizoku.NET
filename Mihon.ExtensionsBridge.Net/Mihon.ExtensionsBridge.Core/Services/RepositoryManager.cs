using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace Mihon.ExtensionsBridge.Core.Services
{

    /// <summary>
    /// Provides operations for managing online <see cref="TachiyomiRepository"/> instances.
    /// Handles listing, addition, removal, and refresh of repositories, and delegates extension-related
    /// update logic to <see cref="IExtensionManager"/>.
    /// </summary>
    public class RepositoryManager : IInternalRepositoryManager
    {

        /// <summary>
        /// Logger used to emit operational and informational messages.
        /// </summary>
        private readonly ILogger _logger;

        private readonly IServiceProvider _serviceProvider;

        private bool _initialized = false;

        /// <summary>
        /// Abstraction for persisting repository and extension data to the working folder structure.
        /// </summary>
        private readonly IWorkingFolderStructure _workingStructure;

        /// <summary>
        /// Manager that compares online and local extensions and performs auto-update operations.
        /// </summary>
        private readonly IInternalExtensionManager _extensionsManager;

        /// <summary>
        /// Synchronization primitive for protecting in-memory repository collection operations.
        /// </summary>
        private readonly SemaphoreSlim _onlineReposLock = new(1, 1);

        /// <summary>
        /// In-memory list of online repositories known to the manager.
        /// Access to this collection must be guarded by <see cref="_onlineReposLock"/>.
        /// </summary>
        private List<TachiyomiRepository> OnlineRepositories { get; set; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="RepositoryManager"/> class.
        /// </summary>
        /// <param name="workingStructure">Working folder structure persisting repositories and extensions.</param>
        /// <param name="repoDownload">Downloader used to populate repositories with extensions.</param>
        /// <param name="extensionsManager">Manager that coordinates extension comparison and auto-update.</param>
        /// <param name="logger">Logger instance for diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown if any provided dependency is <c>null</c>.</exception>
        public RepositoryManager(IServiceProvider provider,
            IWorkingFolderStructure workingStructure,
            IRepositoryDownloader repoDownload,
            IInternalExtensionManager extensionsManager,
            ILogger<RepositoryManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            _workingStructure = workingStructure ?? throw new ArgumentNullException(nameof(workingStructure));
            _extensionsManager = extensionsManager ?? throw new ArgumentNullException(nameof(extensionsManager));
        }

        /// <summary>
        /// Returns a snapshot of currently tracked online repositories.
        /// </summary>
        /// <param name="token">Cancellation token. Not used in this method.</param>
        /// <returns>A new list containing the current set of online repositories.</returns>
        /// <remarks>
        /// The returned list is a copy and is safe to use outside the lock.
        /// </remarks>
        public async Task<List<TachiyomiRepository>> ListOnlineRepositoryAsync(CancellationToken token = default)
        {
            if (!_initialized)
                throw new InvalidOperationException("RepositoryManager is not initialized. Call InitializeAsync() before using this method.");
            await _onlineReposLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var result = new List<TachiyomiRepository>(OnlineRepositories);
                _logger.LogInformation("Listed {Count} online repositories.", result.Count);
                return result;
            }
            finally
            {
                _onlineReposLock.Release();
            }
        }

        /// <summary>
        /// Adds a repository to the online list after populating it with extensions and persisting it to storage.
        /// </summary>
        /// <param name="repository">The repository to add.</param>
        /// <param name="token">Cancellation token to cancel the operation.</param>
        /// <returns><c>true</c> if the repository was added; <c>false</c> if it already existed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="repository"/> is <c>null</c>.</exception>
        /// <remarks>
        /// Duplicate detection is based on the repository <see cref="TachiyomiRepository.Url"/> (case-insensitive).
        /// The method is thread-safe and uses a lock to protect the in-memory collection.
        /// </remarks>
        public async Task<bool> AddOnlineRepositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            if (!_initialized)
                throw new InvalidOperationException("RepositoryManager is not initialized. Call InitializeAsync() before using this method.");
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            repository.Url = MiscExtensions.RepoFromUrl(repository.Url);
            repository.Id = HashingExtensions.SHA256FromUrl(repository.Url);
            await _onlineReposLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (OnlineRepositories.Any(r => r.Url.Equals(repository.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Repository with URL {Url} already exists in online repositories.", repository.Url);
                    return false;
                }
            }
            finally
            {
                _onlineReposLock.Release();
            }

            try
            {
                using (IServiceScope scope = _serviceProvider.CreateScope())
                {
                    var downloader = scope.ServiceProvider.GetRequiredService<IRepositoryDownloader>();
                    repository = await downloader.PopulateExtensionsAsync(repository, token).ConfigureAwait(false);
                }
                await _workingStructure.SaveOnlineRepositoryAsync(repository, token).ConfigureAwait(false);

                await _onlineReposLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (OnlineRepositories.Any(r => r.Url.Equals(repository.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("Repository with URL {Url} already exists in online repositories.", repository.Url);
                        return false;
                    }
                    OnlineRepositories.Add(repository);
                }
                finally
                {
                    _onlineReposLock.Release();
                }

                _logger.LogInformation("Successfully added repository with URL {Url}.", repository.Url);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Adding repository with URL {Url} was canceled.", repository.Url);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding repository with URL {Url}.", repository.Url);
                throw;
            }
        }

        /// <summary>
        /// Removes a repository from the online list based on its URL.
        /// </summary>
        /// <param name="repository">The repository to remove.</param>
        /// <param name="token">Cancellation token. Not used in this method.</param>
        /// <returns><c>true</c> if a matching repository was removed; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="repository"/> is <c>null</c>.</exception>
        /// <remarks>
        /// Removal is performed by comparing <see cref="TachiyomiRepository.Url"/> (case-insensitive). If multiple
        /// entries match, all are removed, and the count is logged.
        /// </remarks>
        public async Task<bool> RemoveOnlineRespositoryAsync(TachiyomiRepository repository, CancellationToken token = default)
        {
            if (!_initialized)
                throw new InvalidOperationException("RepositoryManager is not initialized. Call InitializeAsync() before using this method.");
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            try
            {
                int removedCount;
                await _onlineReposLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    removedCount = OnlineRepositories.RemoveAll(r => r.Url.Equals(repository.Url, StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    _onlineReposLock.Release();
                }

                if (removedCount == 0)
                {
                    _logger.LogInformation("Repository with URL {Url} was not found in online repositories.", repository.Url);
                    return false;
                }

                _logger.LogInformation("Removed {Count} repository entries for URL {Url} from online repositories.", removedCount, repository.Url);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Removing repository with URL {Url} was canceled.", repository.Url);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing repository with URL {Url}.", repository.Url);
                return false;
            }
        }

        /// <summary>
        /// Refreshes all tracked repositories by re-populating their extensions and persisting updates to storage,
        /// then triggers comparison and auto-update of local extensions against the refreshed online data.
        /// </summary>
        /// <param name="token">Cancellation token to cancel the refresh operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// The refresh of each repository is performed in parallel. Persisted updates are handled via
        /// <see cref="IWorkingFolderStructure.SaveExtensionAsync(TachiyomiRepository, CancellationToken)"/>.
        /// After refresh, <see cref="IExtensionManager.CompareOnlineWithLocalAndAutoUpdateAsync(IEnumerable{TachiyomiRepository}, CancellationToken)"/>
        /// is invoked to evaluate and apply local extension updates.
        /// </remarks>
        public async Task RefreshAllRepositoriesAsync(CancellationToken token = default)
        {
            if (!_initialized)
                throw new InvalidOperationException("RepositoryManager is not initialized. Call InitializeAsync() before using this method.");
            List<TachiyomiRepository> snapshot;
            await _onlineReposLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                snapshot = new List<TachiyomiRepository>(OnlineRepositories);
            }
            finally
            {
                _onlineReposLock.Release();
            }

            await Parallel.ForEachAsync(snapshot, async (repo, ct) =>
            {
                try
                {
                    _logger.LogInformation("Refreshing repository {Url}.", repo.Url);
                    TachiyomiRepository? updatedRepo = null;
                    using (IServiceScope scope = _serviceProvider.CreateScope())
                    {
                        var downloader = scope.ServiceProvider.GetRequiredService<IRepositoryDownloader>();
                        updatedRepo = await downloader.PopulateExtensionsAsync(repo, ct).ConfigureAwait(false);
                    }
                    if (updatedRepo != null)
                    {
                        await _workingStructure.SaveOnlineRepositoryAsync(updatedRepo, ct).ConfigureAwait(false);
                        _logger.LogInformation("Successfully refreshed repository {Url}.", repo.Url);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("Refreshing repository {Url} was canceled.", repo.Url);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while refreshing repository {Url}.", repo.Url);
                }
            }).ConfigureAwait(false);

            try
            {
                await _extensionsManager.CompareOnlineWithLocalAndAutoUpdateAsync(snapshot, token).ConfigureAwait(false);
                _logger.LogInformation("Completed comparison and auto-update for {Count} repositories.", snapshot.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Comparison and auto-update operation was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during comparison and auto-update.");
                throw;
            }
        }

        public (TachiyomiRepository?, TachiyomiExtension?) FindRealRepository(TachiyomiExtension extension)
        {
            if (!_initialized)
                throw new InvalidOperationException("RepositoryManager is not initialized. Call InitializeAsync() before using this method.");
            _onlineReposLock.Wait();
            try
            {
                foreach (TachiyomiRepository r in OnlineRepositories)
                {
                    foreach (TachiyomiExtension ext in r.Extensions)
                    {
                        if (ext.Apk == extension.Apk)
                        {
                            _logger.LogInformation("Found repository {Url} for extension APK {Apk}.", r.Url, extension.Apk);
                            return (r, ext);
                        }
                    }
                }
            }
            finally
            {
                _onlineReposLock.Release();
            }
            return (null, null);
        }

        public (TachiyomiRepository?, TachiyomiExtension?) FindRealRepository(TachiyomiRepository repo, TachiyomiExtension ext)
        {
            if (!_initialized)
                throw new InvalidOperationException("RepositoryManager is not initialized. Call InitializeAsync() before using this method.");
            _onlineReposLock.Wait();
            try
            {
                foreach (TachiyomiRepository r in OnlineRepositories)
                {
                    if (r.Id == repo.Id)
                    {
                        foreach (TachiyomiExtension extension in r.Extensions)
                        {
                            if (extension.Apk == ext.Apk)
                            {
                                return (r, extension);
                            }
                        }
                    }
                }
            }
            finally
            {
                _onlineReposLock.Release();
            }
            return (null, null);
        }

        public async Task InitializeAsync(CancellationToken token = default)
        {
            if (!_initialized)
            {
                _onlineReposLock.Wait();
                try
                {
                    _initialized = true;
                    OnlineRepositories = await _workingStructure.LoadOnlineRepositoriesAsync().ConfigureAwait(false);
                }
                finally
                {
                    _onlineReposLock.Release();
                }
            }
        }
    }
}

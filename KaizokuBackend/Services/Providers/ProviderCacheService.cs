using KaizokuBackend.Data;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider storage and caching operations
    /// </summary>
    public class ProviderCacheService
    {
        private readonly AppDbContext _db;
        private readonly SuwayomiClient _suwayomiClient;
        private readonly JobBusinessService _jobBusinessService;
        private readonly SettingsService _settingsService;
        private readonly ILogger<ProviderCacheService> _logger;
        
        private static List<ProviderStorage>? _providers = [];
        private static readonly SemaphoreSlim _providersLock = new(1);

        public ProviderCacheService(AppDbContext db, SuwayomiClient suwayomiClient, JobBusinessService jobBusinessService, 
            SettingsService settingsService, ILogger<ProviderCacheService> logger)
        {
            _db = db;
            _suwayomiClient = suwayomiClient;
            _jobBusinessService = jobBusinessService;
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// Gets cached providers
        /// </summary>
        public async Task<List<ProviderStorage>> GetCachedProvidersAsync(CancellationToken token = default)
        {
            await _providersLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_providers == null || _providers.Count == 0)
                {
                    await RefreshCacheAsync(token).ConfigureAwait(false);
                }
                return _providers ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cached providers");
                return [];
            }
            finally
            {
                _providersLock.Release();
            }
        }

        /// <summary>
        /// Gets sources for specific languages
        /// </summary>
        public async Task<Dictionary<SuwayomiSource, ProviderStorage>> GetSourcesForLanguagesAsync(
            IEnumerable<string>? languages, CancellationToken token = default)
        {
            var storages = (await GetCachedProvidersAsync(token).ConfigureAwait(false))
                .Where(a => !a.IsDisabled).ToList();
            
            var result = new Dictionary<SuwayomiSource, ProviderStorage>();
            var languageSet = new HashSet<string>(languages ?? [], StringComparer.InvariantCultureIgnoreCase);
            
            foreach (var provider in storages)
            {
                foreach (var mapping in provider.Mappings)
                {
                    if (mapping.Source != null)
                    {
                        if (languageSet.Count == 0 || languageSet.Contains(mapping.Source.Lang))
                        {
                            result[mapping.Source] = provider;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Refreshes the provider cache
        /// </summary>
        public async Task RefreshCacheAsync(CancellationToken token = default)
        {
            var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
            if (extensions.Count == 0)
            {
                _providers = [];
                return;
            }

            //Auto Update
            foreach (var extension in extensions)
            {
                if (extension.HasUpdate)
                {
                    await _suwayomiClient.UpdateExtensionAsync(extension.PkgName, token).ConfigureAwait(false);
                }
            }
            extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
            var sources = await _suwayomiClient.GetSourcesAsync(token).ConfigureAwait(false);
            var storages = await _db.Providers.ToListAsync(token).ConfigureAwait(false);
            var newProviders = new List<ProviderStorage>();
            
            foreach (var extension in extensions)
            {
                var provider = await GetOrCreateProviderAsync(sources, storages, extension, token).ConfigureAwait(false);
                if (!provider.IsDisabled && !extension.Installed)
                {
                    await _suwayomiClient.InstallExtensionAsync(extension.PkgName, token).ConfigureAwait(false);
                    provider = await GetOrCreateProviderAsync(sources, storages, extension, token).ConfigureAwait(false);
                }
                await CheckAndScheduleJobsAsync(provider, token).ConfigureAwait(false);
                newProviders.Add(provider);
            }
            
            _providers = newProviders;
            await UpdateExtensionJobsAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates provider cache when an extension is installed/updated
        /// </summary>
        public async Task UpdateExtensionAsync(SuwayomiExtension extension, CancellationToken token = default)
        {
            await _providersLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_providers == null) _providers = [];
                
                var sources = await _suwayomiClient.GetSourcesAsync(token).ConfigureAwait(false);
                var storages = _providers;
                var provider = await GetOrCreateProviderAsync(sources, storages, extension, token).ConfigureAwait(false);
                
                var existing = _providers.FirstOrDefault(p => p.Name == extension.Name && p.Lang == extension.Lang);
                if (existing != null)
                {
                    _providers.Remove(existing);
                }
                
                _providers.Add(provider);
                await CheckAndScheduleJobsAsync(provider, token).ConfigureAwait(false);
                _providers = _providers.OrderBy(p => p.Name).ThenBy(p => p.Lang).ToList();
                await UpdateExtensionJobsAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating extension in cache");
            }
            finally
            {
                _providersLock.Release();
            }
        }

        /// <summary>
        /// Removes provider from cache when extension is uninstalled
        /// </summary>
        public async Task RemoveExtensionAsync(SuwayomiExtension extension, CancellationToken token = default)
        {
            await _providersLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_providers == null) return;
                
                var provider = _providers.FirstOrDefault(p => 
                    p.Name == extension.Name && p.Lang == extension.Lang && p.VersionCode == extension.VersionCode);
                
                if (provider != null)
                {
                    provider.IsDisabled = true;
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                    await UpdateSeriesAndJobsAsync(provider, false, token).ConfigureAwait(false);
                    await UpdateExtensionJobsAsync(token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing extension from cache");
            }
            finally
            {
                _providersLock.Release();
            }
        }

        /// <summary>
        /// Updates provider storage settings
        /// </summary>
        public async Task UpdateProviderStorageAsync(ProviderStorage provider, CancellationToken token = default)
        {
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        private async Task<ProviderStorage> GetOrCreateProviderAsync(List<SuwayomiSource> sources,
            List<ProviderStorage> storages, SuwayomiExtension extension, CancellationToken token = default)
        {
            var provider = storages.FirstOrDefault(p =>
                p.Name == extension.Name && p.Lang == extension.Lang && p.VersionCode == extension.VersionCode);
                
            if (provider == null || provider.Mappings.Count == 0)
            {
                provider = new ProviderStorage
                {
                    Name = extension.Name,
                    Lang = extension.Lang,
                    ApkName = extension.ApkName,
                    PkgName = extension.PkgName,
                    VersionCode = extension.VersionCode,
                    IsDisabled = !extension.Installed,
                    IsStorage = true
                };

                // Handle version updates
                var existingProvider = storages.FirstOrDefault(p => p.Name == extension.Name && p.Lang == extension.Lang);
                if (existingProvider != null)
                {
                    provider.IsStorage = existingProvider.IsStorage;
                    provider.In
                    _db.Providers.Remove(existingProvider);
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                // Get sources for this extension
                var extensionSources = extension.Lang == "all" 
                    ? sources.Where(s => s.Name == extension.Name).ToList()
                    : sources.Where(s => s.Name == extension.Name && s.Lang == extension.Lang).ToList();
                
                var mappings = new ConcurrentBag<Mappings>();
                await Parallel.ForEachAsync(extensionSources,
                    new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = token },
                    async (source, ct) =>
                    {
                        try
                        {
                            var preferences = await _suwayomiClient.GetSourcePreferencesAsync(source.Id, ct)
                                .ConfigureAwait(false);
                            RemoveSuffixPreferences(extension.Lang, source.Id, preferences);
                            if (mappings.All(a => a.Source?.Id != source.Id))
                            {
                                mappings.Add(new Mappings
                                {
                                    Preferences = preferences,
                                    Source = source,
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unable to retrieve preferences for source {SourceName}", source.Name);
                        }
                    }).ConfigureAwait(false);
                
                provider.IsDisabled = false;
                provider.Mappings = mappings.ToList();
                _db.Providers.Add(provider);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }

            if (provider.IsDisabled != !extension.Installed)
            {
                provider.IsDisabled = !extension.Installed;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            
            return provider;
        }

        private async Task CheckAndScheduleJobsAsync(ProviderStorage provider, CancellationToken token = default)
        {
            if (provider.Mappings.Any(m => m.Source?.SupportsLatest ?? false))
            {
                var jobStatus = await _jobBusinessService.GetJobStatusAsync(JobType.GetLatest, $"{provider.Name}|{provider.Lang}", token).ConfigureAwait(false);
                if ((jobStatus == null || !jobStatus.Value) && !provider.IsDisabled)
                {
                    await UpdateSeriesAndJobsAsync(provider, true, token).ConfigureAwait(false);
                }
                else if (jobStatus.HasValue && jobStatus.Value && provider.IsDisabled)
                {
                    await UpdateSeriesAndJobsAsync(provider, false, token).ConfigureAwait(false);
                }
            }
        }

        private async Task UpdateSeriesAndJobsAsync(ProviderStorage provider, bool enable, CancellationToken token = default)
        {
            var settings = await _settingsService.GetSettingsAsync(token).ConfigureAwait(false);
            var seriesProviders = await _db.SeriesProviders.Where(sp => sp.Provider == provider.Name).ToListAsync(token).ConfigureAwait(false);
            
            var providersToUpdate = new List<SeriesProvider>();
            foreach (var seriesProvider in seriesProviders)
            {
                if (seriesProvider.IsUninstalled != !enable)
                {
                    providersToUpdate.Add(seriesProvider);
                    seriesProvider.IsUninstalled = !enable;
                }
            }
            
            if (providersToUpdate.Count > 0)
            {
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            
            foreach (var seriesProvider in providersToUpdate)
            {
                await _jobBusinessService.ManageSeriesProviderJobAsync(seriesProvider, true, false, token).ConfigureAwait(false);
            }

            var supportedSources = provider.Mappings
                .Select(m => m.Source)
                .Where(s => s != null && settings.PreferredLanguages.Contains(s.Lang))
                .ToList();
                
            foreach (var source in supportedSources)
            {
                if (source != null && source.SupportsLatest)
                {
                    await _jobBusinessService.ManageSourceJobAsync(source, enable, true, token).ConfigureAwait(false);
                }
            }
        }

        private async Task UpdateExtensionJobsAsync(CancellationToken token = default)
        {
            if (_providers == null) return;
            
            bool hasEnabledProviders = _providers.Any(p => !p.IsDisabled);
            await _jobBusinessService.ManageExtensionUpdatesAsync(hasEnabledProviders, token).ConfigureAwait(false);
        }

        private static void RemoveSuffixPreferences(string extensionLang, string sourceId, List<SuwayomiPreference> preferences)
        {
            preferences.ForEach(pref =>
            {
                if (extensionLang == "all")
                {
                    int lastUnderscore = pref.props.key.LastIndexOf('_');
                    if (lastUnderscore > 0)
                    {
                        pref.props.key = pref.props.key.Substring(0, lastUnderscore);
                    }
                }
                pref.Source = sourceId;
            });
        }
    }
}
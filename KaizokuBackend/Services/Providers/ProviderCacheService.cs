using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using System.Linq;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider storage and caching operations
    /// </summary>
    public class ProviderCacheService
    {
        private readonly AppDbContext _db;
        private readonly MihonBridgeService _mihon;
        private readonly JobBusinessService _jobBusinessService;
        private readonly SettingsService _settingsService;
        private readonly ILogger<ProviderCacheService> _logger;
        
        private static List<ProviderStorageEntity>? _providers = [];
        private static List<string> _languages = [];

        public ProviderCacheService(AppDbContext db, JobBusinessService jobBusinessService,
            SettingsService settingsService, MihonBridgeService mihon, ILogger<ProviderCacheService> logger)
        {
            _db = db;
            _jobBusinessService = jobBusinessService;
            _settingsService = settingsService;
            _mihon = mihon;
            _logger = logger;
        }

        /// <summary>
        /// Gets cached providers
        /// </summary>
        public async Task<List<ProviderStorageEntity>> GetCachedProvidersAsync(CancellationToken token = default)
        {
            if (_providers == null || _providers.Count == 0)
            {
                await RefreshCacheAsync(false, token).ConfigureAwait(false);
            }
            return _providers ?? [];
        }

        public async Task<List<SmallProviderDto>> GetCachedProviderSummariesAsync(CancellationToken token = default)
        {
            var providers = await GetCachedProvidersAsync(token).ConfigureAwait(false);
            return providers.Select(p => p.ToSmallProviderDto()).ToList();
        }
        public async Task<List<string>> GetAvailableLanguagesAsync(CancellationToken token = default)
        {
            if (_providers == null || _providers.Count == 0)
            {
                await RefreshCacheAsync(false, token).ConfigureAwait(false);
            }
            return _languages ?? [];
        }

        /// <summary>
        /// Gets sources for specific languages
        /// </summary>
        public async Task<List<ProviderStorageEntity>> GetSourcesForLanguagesAsync(
            IEnumerable<string>? languages, CancellationToken token = default)
        {
            var storages = (await GetCachedProvidersAsync(token).ConfigureAwait(false))
                .Where(a => a.IsActive).ToList();

            var result = new HashSet<ProviderStorageEntity>();
            var languageSet = new HashSet<string>(languages ?? [], StringComparer.InvariantCultureIgnoreCase);


            foreach (var provider in storages)
            {
                if (languageSet.Count == 0)
                {
                    if (!result.Contains(provider))
                        result.Add(provider);
                }
                else
                {
                    if (provider.Language == "all" || languageSet.Contains(provider.Language))
                    {
                        if (!result.Contains(provider))
                            result.Add(provider);
                    }
                }
            }
            return result.ToList();
        }

        public async Task<List<(string MihonProviderId, SmallProviderDto Summary)>> GetProviderSummariesForLanguagesAsync(
            IEnumerable<string>? languages, CancellationToken token = default)
        {
            var storages = await GetSourcesForLanguagesAsync(languages, token).ConfigureAwait(false);
            return storages
                .Where(s => !string.IsNullOrEmpty(s.MihonProviderId))
                .Select(s => (s.MihonProviderId!, s.ToSmallProviderDto()))
                .ToList();
        }

        public async Task UpdateAllExtensionsAsync(CancellationToken token = default)
        {
            _logger.LogInformation("Updating all extensions and refreshing provider cache...");
            await _mihon.RefreshAllRepositoriesAsync(token).ConfigureAwait(false);
            await RefreshCacheAsync(false, token).ConfigureAwait(false);
            _logger.LogInformation("All extensions updated and provider cache refreshed.");
        }

        public async Task<bool> ReconcileLocalAsync(string package, string[] prefLanguages, RepositoryGroup? grp, List<ProviderStorageEntity> storages, List<TachiyomiRepository> onlinerepos, CancellationToken token = default)
        {
            bool commit = false;
            if (grp == null)
            {
                // Mark as dead if the provider (enabled/disabled state maintained)
                foreach (var dead in storages.Where(a => a.SourcePackageName == package && !a.IsDead))
                {
                    dead.IsDead = true;
                    commit = true;
                }
            }
            else
            {
                RepositoryEntry entry = grp.GetActiveEntry();
                TachiyomiExtension extension = entry.Extension;
                foreach (TachiyomiSource source in extension.Sources)
                {
                    string repoName = onlinerepos.FirstOrDefault(a => a.Id == entry.RepositoryId)?.Name ?? "Removed";
                    ProviderStorageEntity? p = storages.FirstOrDefault(a => a.SourcePackageName == package && a.SourceSourceId == source.Id);
                    if (p != null)
                    {
                        if (p.IsDead)
                        {
                            // Clear dead flag since the provider is back in the ecosystem,
                            // if it was enabled, it will back to life, if not keep uninstalled.
                            // The database is the source of truth for install state.
                            p.IsDead = false;
                            p.SourceRepositoryId = entry.RepositoryId;
                            p.SourceRepositoryName = repoName;
                            commit = true;
                        }
                        continue;
                    }
                    ProviderStorageEntity storage = new ProviderStorageEntity();
                    storage.MihonProviderId = entry.GetMihonProviderId(source);
                    ISourceInterop? interop = await _mihon.SourceFromProviderIdAsync(storage.MihonProviderId, token).ConfigureAwait(false);
                    storage.FillProviderStorage(entry, extension, source, interop, repoName, entry.RepositoryId);
                    if (storage.Language == "all")
                        storage.IsEnabled = true;
                    else if (prefLanguages.Contains(storage.Language, StringComparer.InvariantCultureIgnoreCase))
                        storage.IsEnabled = true;
                    else
                        storage.IsEnabled = false;
                    _db.Providers.Add(storage);
                    commit = true;
                }
            }
            return commit;
        }

        /// <summary>
        /// Refreshes the provider cache
        /// </summary>
        public async Task RefreshCacheAsync(bool skipSchedule = false, CancellationToken token = default)
        {
            var settings = await _settingsService.GetSettingsAsync(token).ConfigureAwait(false);
            var extensions = _mihon.ListExtensions();
            var onlinerepos = _mihon.ListOnlineRepositories();
            var storages = await _db.Providers.ToListAsync(token).ConfigureAwait(false);
            //First pass, reconcile storage sources with installed extensions
            List<string> packages = storages.Select(a=>a.SourcePackageName).Where(a => a != null).Select(a => a!).ToList();
            packages.AddRange(extensions.Select(a => a.GetActiveEntry().Extension.Package));
            packages = packages.Distinct().ToList();
            bool commit = false;
            foreach (string package in packages)
            {
                RepositoryGroup? grp = extensions.FirstOrDefault(a => a.GetActiveEntry().Extension.Package.Equals(package, StringComparison.OrdinalIgnoreCase));
                commit |= await ReconcileLocalAsync(package, settings.PreferredLanguages, grp, storages, onlinerepos, token).ConfigureAwait(false);
            }
            if (commit)
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            storages = await _db.Providers.ToListAsync(token).ConfigureAwait(false);
            //Second pass, reconcile storage source with online repositories.
            commit = false;
            foreach (ProviderStorageEntity storage in storages)
            {
                var combo = extensions.FindSource(storage.MihonProviderId);
                if (combo.Group == null)
                {
                    if (storage.IsEnabled)
                    {
                        var onlinecombo = onlinerepos.FindSource(storage.MihonProviderId);
                        if (onlinecombo.Extension == null && !storage.IsDead)
                        {
                            storage.IsDead = true;
                            storage.IsEnabled = false;
                            commit = true;
                            await _db.SaveChangesAsync(token).ConfigureAwait(false);
                        }
                        else if (onlinecombo.Extension != null)
                        {
                            var repoGroup = await _mihon.AddExtensionAsync(onlinecombo.Extension,false, token).ConfigureAwait(false);
                            if (repoGroup != null)
                            {
                                commit |= await ReconcileLocalAsync(storage.SourcePackageName!, settings.PreferredLanguages, repoGroup, storages, onlinerepos, token).ConfigureAwait(false);                               
                            }
                            else
                            {
                                storage.IsEnabled = false;
                                storage.IsBroken = true;
                                commit = true;
                            }

                        }
                    }
                }
            }
            if (commit)
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _providers = storages;
            List<string> languages = storages.Where(a=>a.IsActive).Select(a=>a.Language).Distinct().ToList();
            if (languages.Contains("all"))
            {
                languages.Remove("all");
                foreach (var language in settings.PreferredLanguages)
                {
                    if (!languages.Contains(language))
                        languages.Add(language);
                }
            }
            languages = languages.OrderBy(a => a).ToList();
            _languages = languages;
            if (!skipSchedule)
            {
                foreach (ProviderStorageEntity p in storages)
                {
                    await CheckAndScheduleJobsAsync(p, token).ConfigureAwait(false);
                }
                await UpdateExtensionJobsAsync(token).ConfigureAwait(false);
            }
        }

        private async Task CheckAndScheduleJobsAsync(ProviderStorageEntity provider, CancellationToken token = default)
        {

            if (provider.SupportLatest)
            {
                var jobStatus = await _jobBusinessService.GetJobStatusAsync(JobType.GetLatest, provider.MihonProviderId, token).ConfigureAwait(false);
                if ((jobStatus == null || !jobStatus.Value) && provider.IsActive)
                {
                    await UpdateSeriesAndJobsAsync(provider, true, token).ConfigureAwait(false);
                }
                else if (jobStatus.HasValue && jobStatus.Value && !provider.IsActive)
                {
                    await UpdateSeriesAndJobsAsync(provider, false, token).ConfigureAwait(false);
                }
            }

        }

        private async Task UpdateSeriesAndJobsAsync(ProviderStorageEntity provider, bool enable, CancellationToken token = default)
        {
            var settings = await _settingsService.GetSettingsAsync(token).ConfigureAwait(false);
            var seriesProviders = await _db.SeriesProviders.Where(sp => sp.MihonProviderId == provider.MihonProviderId).ToListAsync(token).ConfigureAwait(false);
            
            var providersToUpdate = new List<SeriesProviderEntity>();
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

            await _jobBusinessService.ManageSourceJobAsync(provider, enable, true, token).ConfigureAwait(false);
        }

        private async Task UpdateExtensionJobsAsync(CancellationToken token = default)
        {
            if (_providers == null) return;
            
            bool hasEnabledProviders = _providers.Any(p => p.IsActive);
            await _jobBusinessService.ManageExtensionUpdatesAsync(hasEnabledProviders, token).ConfigureAwait(false);
        }

    
    }
}

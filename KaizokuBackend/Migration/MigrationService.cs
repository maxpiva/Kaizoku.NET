using KaizokuBackend.Data;
using KaizokuBackend.Migration.Models;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Runtime;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using sun.misc;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LegacyProvider = KaizokuBackend.Migration.Models.ProviderStorage;
using LegacySuwayomiPreference = KaizokuBackend.Migration.Models.SuwayomiPreference;
using LegacySuwayomiSource = KaizokuBackend.Migration.Models.SuwayomiSource;
using NewProviderStorage = KaizokuBackend.Models.Database.ProviderStorageEntity;
using SuwayomiSource = KaizokuBackend.Migration.Models.SuwayomiSource;

namespace KaizokuBackend.Migration;

/// <summary>
/// Coordinates the one-time migration from the legacy <c>kaizoku.db</c> file to the new <c>kaizoku2.0.db</c> schema.
/// </summary>
public class MigrationService
{
    private static readonly char[] PipeSeparator = ['|'];
    private static readonly char[] LanguageSeparators = ['|', ','];

    private readonly MihonBridgeService _mihon;
    private readonly ILogger<MigrationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private ProviderCacheService _providerCache;
    private ThumbCacheService _thumbs;
    
    public MigrationService(IServiceProvider factory, MihonBridgeService mihon, ILogger<MigrationService> logger,
                IConfiguration configuration)
    {
        _mihon = mihon;
        _serviceProvider = factory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration;

    }


    private bool Version2Database(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        bool hasVersion2 = false;
        using (SqliteConnection con = new SqliteConnection(cs))
        {
            con.Open();
            hasVersion2 = ColumnExists(con, "ETagCache", "Url");

        }
        return hasVersion2;
    }

    private static bool ColumnExists(SqliteConnection con, string tableName, string columnName)
    {
        bool result = false;
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({EscapeIdent(tableName)});";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(r.GetOrdinal("name"));
                if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                }
            }
        }
        /*
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
            cmd.ExecuteNonQuery();
        }*/
        return result;

    }
    static string EscapeIdent(string ident) => ident.Replace("\"", "\"\"");


    /// <summary>
    /// Executes the migration when a legacy database is discovered.
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        string? newDatabasePath = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(newDatabasePath))
        {
            _logger.LogError("DefaultConnection string is not set in configuration; cannot determine database.");
            return false;
        }
        if (!newDatabasePath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("DefaultConnection string is not in expected format 'Data Source=path'; cannot determine database.");
            return false;
        }
        newDatabasePath = newDatabasePath.Substring("Data Source=".Length).Trim();
        newDatabasePath = Path.GetFullPath(newDatabasePath);
        if (!File.Exists(newDatabasePath))
        {
            _logger.LogError("No existing database found at {Path}. Assuming new installation.", newDatabasePath);
            var newDbOptions2 = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={newDatabasePath}")
                .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll)
                .Options;
            await using var targetDb2 = new AppDbContext(newDbOptions2);
            await targetDb2.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (Version2Database(newDatabasePath))
        {
            return true;
        }
        _logger.LogInformation("Kaizoku.NET v1.0 Database found, starting migration...");
        string directoryPath = Path.GetDirectoryName(newDatabasePath);
        string dbFile = Path.GetFileName(newDatabasePath);
        string dbFileWithoutExtension = Path.GetFileNameWithoutExtension(dbFile);
        string extension = Path.GetExtension(dbFile);
        string legacyDatabasePath = Path.Combine(directoryPath, dbFileWithoutExtension + "_1.0_backup" + extension);
        try
        {
            File.Move(newDatabasePath, legacyDatabasePath);
        }
        catch(Exception e)
        {
            _logger.LogError("Unable to backup original Kaizoku.NET 1.0 Database.");
            return false;
        }

        var oldDbOptions = new DbContextOptionsBuilder<OldDbContext>()
            .UseSqlite($"Data Source={legacyDatabasePath}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        var newDbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={newDatabasePath}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll)
            .Options;

        await using var legacyDb = new OldDbContext(oldDbOptions);
        await using var targetDb = new AppDbContext(newDbOptions);

        if (File.Exists(newDatabasePath))
        {
            _logger.LogInformation("Removing previously generated database at {Path}", newDatabasePath);
            await targetDb.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
        }

        await targetDb.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        MigrationState state = new();
        await MigrateSettingsAsync(legacyDb, targetDb, state, cancellationToken).ConfigureAwait(false);
        _providerCache = _serviceProvider.GetRequiredService<ProviderCacheService>();
        _thumbs = _serviceProvider.GetRequiredService<ThumbCacheService>();
        await MigrateProvidersAsync(legacyDb, targetDb, state, cancellationToken).ConfigureAwait(false);
        await MigrateSeriesProvidersAsync(legacyDb, targetDb, state, cancellationToken).ConfigureAwait(false);
        await MigrateSeriesAsync(legacyDb, targetDb, state, cancellationToken).ConfigureAwait(false);
        await MigrateJobsAsync(legacyDb, targetDb, state, cancellationToken).ConfigureAwait(false);
        
        _logger.LogInformation("Legacy database migration finished.");
        return true;
    }



    private async Task MigrateSettingsAsync(OldDbContext legacyDb, AppDbContext targetDb, MigrationState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Step {step}/{total}: Migrating settings...",1,5);

        var legacySettings = await legacyDb.Settings
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        targetDb.Settings.RemoveRange(targetDb.Settings);
        await targetDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var setting in legacySettings)
        {
            targetDb.Settings.Add(new KaizokuBackend.Models.Database.SettingEntity
            {
                Name = setting.Name,
                Value = setting.Value
            });

            if (string.Equals(setting.Name, "MihonRepositories", StringComparison.OrdinalIgnoreCase))
            {
                state.MihonRepositories = ParseDelimited(setting.Value, PipeSeparator);
            }
            else if (string.Equals(setting.Name, "PreferredLanguages", StringComparison.OrdinalIgnoreCase))
            {
                state.Languages = ParseDelimited(setting.Value, LanguageSeparators);
            }
        }

        await targetDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRepositoriesRegisteredAsync(state.MihonRepositories, cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateProvidersAsync(OldDbContext legacyDb, AppDbContext targetDb, MigrationState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Step {step}/{total}: Migrating providers and installing extensions...",2,5);
        var legacyProviders = await legacyDb.Providers.Where(a=>!a.IsDisabled)            
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (legacyProviders.Count == 0)
        {
            _logger.LogInformation("No enabled providers were found in the legacy database; skipping provider migration.");
            return;
        }
        var repositories = _mihon.ListOnlineRepositories();

        
        var insertedProviders = new List<NewProviderStorage>(legacyProviders.Count);
        int cnt = 0;
        foreach (var legacyProvider in legacyProviders)
        {
            TachiyomiExtension? repo = repositories.SelectMany(a => a.Extensions).FirstOrDefault(repo => repo.Package.Equals(legacyProvider.PkgName, StringComparison.OrdinalIgnoreCase));
            if (repo == null)
            {
                _logger.LogWarning("No repository contains the package {Package} for provider {Provider}; skipping.", legacyProvider.PkgName, legacyProvider.Name);
                continue;
            }
            RepositoryGroup? grp = await _mihon.AddExtensionAsync(repo, false, cancellationToken).ConfigureAwait(false);
            if (grp == null)
            {
                _logger.LogWarning("Failed to install extension for package {Package} (provider {Provider}); skipping.", legacyProvider.PkgName, legacyProvider.Name);
                continue;
            }
            cnt++;
            await ApplyProviderPreferencesAsync(grp, legacyProvider, cancellationToken).ConfigureAwait(false);
            state.ProviderPackageLookup[legacyProvider.Name] = legacyProvider.PkgName;
        }
        await _providerCache.RefreshCacheAsync(true, cancellationToken);
       
        _logger.LogInformation("Migrated {Count} providers into the new database.", cnt);
    }

    private async Task MigrateSeriesProvidersAsync(OldDbContext legacyDb, AppDbContext targetDb, MigrationState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Step {step}/{total}: Migrating series providers...",3,5);

        var legacyProviders = await legacyDb.Providers
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var legacySeriesProviders = await legacyDb.SeriesProviders
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (legacySeriesProviders.Count == 0)
        {
            _logger.LogInformation("Legacy database does not contain any series providers; skipping step 3.");
            return;
        }

        var repositories = _mihon.ListOnlineRepositories();
        if (repositories.Count == 0)
        {
            _logger.LogWarning("Cannot migrate series providers because no Mihon repositories are available.");
            return;
        }

        var runtimeContexts = new Dictionary<string, ProviderRuntimeContext>(StringComparer.OrdinalIgnoreCase);
        var migratedProviders = new List<KaizokuBackend.Models.Database.SeriesProviderEntity>(legacySeriesProviders.Count);

        var cachedProviders = await _providerCache.GetCachedProvidersAsync(cancellationToken);

        foreach (var legacyProvider in legacySeriesProviders)
        {
            var thumbDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LegacyProvider? legacy = legacyProvider.Provider == "unknown" ? null : legacyProviders.First(p => string.Equals(p.Name, legacyProvider.Provider, StringComparison.OrdinalIgnoreCase));
            var context = await GetProviderRuntimeContextAsync(legacyProvider, legacy, state, cachedProviders, runtimeContexts, cancellationToken).ConfigureAwait(false);
            var destination = await ConvertSeriesProviderAsync(legacyProvider, context, thumbDictionary, state, cancellationToken).ConfigureAwait(false);
            migratedProviders.Add(destination);

            foreach (var mapping in thumbDictionary)
            {
                state.ThumbnailLookup[mapping.Key] = mapping.Value;
            }
        }
        foreach (var mig in migratedProviders.Where(a => a.IsLocal || a.IsUnknown))
        {
            if (mig.IsCover)
            {
                var nprov = migratedProviders.FirstOrDefault(a => !a.IsLocal && !a.IsUnknown && a.SeriesId == mig.SeriesId);
                if (nprov != null)
                {
                    nprov.IsCover = true;
                }
                mig.IsCover = false;
            }
        }

        state.SeriesProviders = migratedProviders;
        await targetDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Migrated {Count} series providers into the new database.", migratedProviders.Count);
    }

    private async Task MigrateSeriesAsync(OldDbContext legacyDb, AppDbContext targetDb, MigrationState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Step {step}/{total}: Migrating series metadata...",4,5);

        var legacySeries = await legacyDb.Series
            .AsNoTracking()
            .Include(s => s.Sources)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (legacySeries.Count == 0)
        {
            _logger.LogInformation("No legacy series entries found; skipping step 4.");
            return;
        }

        var migratedSeries = new List<KaizokuBackend.Models.Database.SeriesEntity>(legacySeries.Count);

        foreach (var series in legacySeries)
        {
            var destination = new KaizokuBackend.Models.Database.SeriesEntity
            {
                Id = series.Id,
                Title = series.Title ?? string.Empty,
                ThumbnailUrl = ResolveSeriesThumbnail(series, state),
                Artist = series.Artist ?? string.Empty,
                Author = series.Author ?? string.Empty,
                Description = series.Description ?? string.Empty,
                Genre = series.Genre,
                Status = series.Status,
                StoragePath = series.StoragePath ?? string.Empty,
                Type = series.Type,
                ChapterCount = series.ChapterCount,
                PauseDownloads = series.PauseDownloads,
                StartFromChapter = null,
            };

            migratedSeries.Add(destination);
        }

        await targetDb.Series.AddRangeAsync(migratedSeries, cancellationToken).ConfigureAwait(false);
        foreach (var series in state.SeriesProviders)
        {
            if (series.ThumbnailUrl != null)
               await _thumbs.AddUrlAsync(series.ThumbnailUrl, series.MihonProviderId, cancellationToken).ConfigureAwait(false);
        }
        await targetDb.SeriesProviders.AddRangeAsync(state.SeriesProviders, cancellationToken).ConfigureAwait(false);


        await targetDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Migrated {Count} series entries into the new database.", migratedSeries.Count);
    }

    private async Task MigrateJobsAsync(OldDbContext legacyDb, AppDbContext targetDb, MigrationState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Step {step}/{total}: Migrating jobs...",5,5);
        List<KaizokuBackend.Migration.Models.Job> jobs = await legacyDb.Jobs
        .AsNoTracking()
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);

        var providers = await targetDb.Providers
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<KaizokuBackend.Models.Database.JobEntity> migratedJobs = new List<KaizokuBackend.Models.Database.JobEntity>(jobs.Count);
        foreach (var job in jobs)
        {
            KaizokuBackend.Models.Database.JobEntity newJob = new KaizokuBackend.Models.Database.JobEntity
            {
                Id = job.Id,
                JobType = job.JobType,
                JobParameters = job.JobParameters,
                Key = job.Key,
                GroupKey = job.GroupKey,
                TimeBetweenJobs = job.TimeBetweenJobs,
                MinutePlace = job.MinutePlace,
                IsEnabled = job.IsEnabled,
                Priority = job.Priority,
                PreviousExecution = job.PreviousExecution,
                NextExecution = job.NextExecution
            };
            if (newJob.JobType==JobType.GetChapters)
            {
                string id = JsonSerializer.Deserialize<string>(newJob.JobParameters);
                Guid spid = Guid.Parse(id);
                var p = state.SeriesProviders.FirstOrDefault(a => a.Id == spid);
                if (p.IsLocal || p.IsUnknown)
                    continue;
            }
            if (newJob.JobType == JobType.GetLatest)
            {
                LegacySuwayomiSource? source = JsonSerializer.Deserialize<LegacySuwayomiSource>(newJob.JobParameters);
                string end = "|" + source.Id;
                NewProviderStorage? prov = providers.FirstOrDefault(p => p.MihonProviderId.EndsWith(end));
                if (prov==null)
                    prov = providers.FirstOrDefault(p => p.Name == source.DisplayName && p.Language==source.Lang);
                if (prov==null)
                    prov = providers.FirstOrDefault(p => p.Name == source.DisplayName && p.Language == "all");
                if (prov==null)
                {
                    _logger.LogWarning("Could not find provider for job {JobId} with source {SourceId}; provider was {ProviderName} ({ProviderLanguage}). ", newJob.Id, source.Id, source.DisplayName, source.Lang);
                    continue;
                }
                newJob.JobParameters = JsonSerializer.Serialize(prov.MihonProviderId);
                newJob.GroupKey = $"{prov.Name}|{prov.Language}";
                newJob.Key = prov.MihonProviderId;
            }
            migratedJobs.Add(newJob);
        }
        await targetDb.Jobs.AddRangeAsync(migratedJobs, cancellationToken).ConfigureAwait(false);
        await targetDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Migrated {Count} series entries into the new database.", migratedJobs.Count);
    }



    private async Task EnsureRepositoriesRegisteredAsync(IReadOnlyCollection<string> repositories, CancellationToken cancellationToken)
    {
        if (repositories.Count == 0)
        {
            _logger.LogWarning("No Mihon repositories were defined in legacy settings; bridge repositories were not updated.");
            return;
        }

        foreach (var repositoryUrl in repositories)
        {
            try
            {
                var repo = new TachiyomiRepository(repositoryUrl)
                {
                    Name = repositoryUrl,
                    Id = repositoryUrl
                };

                await _mihon.AddOnlineRepositoryAsync(repo, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Registered repository {Repository}", repositoryUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register repository {Repository}", repositoryUrl);
            }
        }
    }



    private static (TachiyomiRepository Repository, TachiyomiExtension Extension)? FindExtensionMatch(IEnumerable<TachiyomiRepository> repositories, string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var candidates = repositories
            .SelectMany(repo => repo.Extensions.Select(ext => (repo, ext)))
            .Where(tuple => packageId.Equals(tuple.ext.Package, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(tuple => tuple.ext.VersionCode)
            .ThenByDescending(tuple => tuple.ext.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return (candidates[0].repo, candidates[0].ext);
    }

    private async Task ApplyProviderPreferencesAsync(RepositoryGroup group, LegacyProvider legacyProvider, CancellationToken cancellationToken)
    {
        if (legacyProvider.Mappings == null || legacyProvider.Mappings.Count == 0)
        {
            return;
        }

        IExtensionInterop? interop = null;
        try
        {
            interop = await _mihon.GetInteropAsync(group, cancellationToken).ConfigureAwait(false);
            if (interop == null)
            {
                return;
            }

            foreach (var mapping in legacyProvider.Mappings)
            {
                if (mapping.Source == null)
                {
                    continue;
                }

                var sourceInterop = ResolveSourceInterop(interop, mapping.Source);
                if (sourceInterop == null)
                {
                    _logger.LogDebug("Source {Source} not found in extension {Package}.", mapping.Source.Id, legacyProvider.PkgName);
                    continue;
                }

                ApplyPreferences(sourceInterop, mapping.Preferences);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply preferences for provider {Provider}.", legacyProvider.Name);
        }

    }

    private static ISourceInterop? ResolveSourceInterop(IExtensionInterop interop, LegacySuwayomiSource source)
    {
        if (source == null)
        {
            return null;
        }

        if (long.TryParse(source.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
        {
            var byId = interop.Sources.FirstOrDefault(s => s.Id == parsedId);
            if (byId != null)
            {
                return byId;
            }
        }
        return null;
    }

    private static void ApplyPreferences(ISourceInterop sourceInterop, List<LegacySuwayomiPreference> legacyPreferences)
    {
        if (legacyPreferences == null || legacyPreferences.Count == 0)
        {
            return;
        }

        var currentPreferences = sourceInterop.GetPreferences();
        if (currentPreferences == null || currentPreferences.Count == 0)
        {
            return;
        }

        var indexLookup = currentPreferences
            .Select((pref, index) => new { pref, index })
            .Where(item => !string.IsNullOrWhiteSpace(item.pref.Key))
            .ToDictionary(item => item.pref.Key!, item => item.index, StringComparer.OrdinalIgnoreCase);

        foreach (var legacyPreference in legacyPreferences)
        {
            var key = legacyPreference?.props?.key;
            if (string.IsNullOrEmpty(key))
                continue;
            if (!indexLookup.TryGetValue(key, out var position))
            {
                int p = key.LastIndexOf("_");
                key = key.Substring(0, p);
                if (!indexLookup.TryGetValue(key, out position))
                    continue;
            }

            var value = NormalizePreferenceValue(legacyPreference);
            if (value == "!empty-value!")
                value = "";
            if (currentPreferences[position].CurrentValue!=value)
                sourceInterop.SetPreference(position, value);
        }
    }

    private static string NormalizePreferenceValue(LegacySuwayomiPreference? preference)
    {
        if (preference?.props == null)
        {
            return string.Empty;
        }

        var currentValue = preference.props.currentValue;
        if (currentValue is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => element.ToString()
            };
        }

        if (currentValue != null)
        {
            return currentValue.ToString() ?? string.Empty;
        }

        return preference.props.defaultValue?.ToString() ?? string.Empty;
    }

    private static string NormalizeLanguage(string? language)
    {
        return string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim().ToLowerInvariant();
    }

    private async Task<KaizokuBackend.Models.Database.SeriesProviderEntity> ConvertSeriesProviderAsync(
        Models.SeriesProvider legacyProvider,
        ProviderRuntimeContext context,
        Dictionary<string, string> thumbDictionary,
        MigrationState state,
        CancellationToken cancellationToken)
    {
        var destination = new KaizokuBackend.Models.Database.SeriesProviderEntity
        {
            Id = legacyProvider.Id,
            SeriesId = legacyProvider.SeriesId,
            Provider = string.IsNullOrWhiteSpace(legacyProvider.Provider) ? "Unknown" : legacyProvider.Provider,
            Scanlator = legacyProvider.Scanlator,
            Url = legacyProvider.Url,
            Title = legacyProvider.Title,
            Language = legacyProvider.Language,
            ThumbnailUrl = legacyProvider.ThumbnailUrl,
            Artist = legacyProvider.Artist,
            Author = legacyProvider.Author,
            Description = legacyProvider.Description,
            Genre = legacyProvider.Genre?.ToList() ?? new List<string>(),
            FetchDate = legacyProvider.FetchDate,
            ChapterCount = legacyProvider.ChapterCount,
            ContinueAfterChapter = legacyProvider.ContinueAfterChapter,
            IsTitle = legacyProvider.IsTitle,
            IsCover = legacyProvider.IsCover,
            IsLocal = false,
            IsUnknown = legacyProvider.IsUnknown,
            IsStorage = legacyProvider.IsStorage,
            IsDisabled = legacyProvider.IsDisabled,
            IsUninstalled = legacyProvider.IsUninstalled,
            Status = legacyProvider.Status,
            Chapters = legacyProvider.Chapters?.Select(MapChapter).ToList() ?? new List<KaizokuBackend.Models.Chapter>()
        };
        ISourceInterop? sourceInterop = null;
        NewProviderStorage? ps = context.GetProviderStorage(legacyProvider.Language);
        if (ps != null)
        {
            sourceInterop = await context.GetSourceInteropAsync(destination.Provider, legacyProvider.Language, cancellationToken).ConfigureAwait(false);
            if (sourceInterop == null)
            {
                destination.IsLocal = true;
                _logger.LogWarning("Source interop unavailable for provider {Provider}; for series {SeriesId}.", legacyProvider.Provider, legacyProvider.SeriesId);
                destination.Provider = legacyProvider.Provider;
                destination.IsUnknown = legacyProvider.Provider == "Unknown";
                destination.ThumbnailUrl = null;
                return destination;
            }
        }
        else
        {
            destination.IsLocal = true;
            destination.Provider = legacyProvider.Provider;
            destination.IsUnknown = legacyProvider.Provider == "Unknown";
            destination.ThumbnailUrl = null;
            return destination;
        }
        destination.MihonProviderId = ps.MihonProviderId;
        var resolvedManga = await TryResolveMangaAsync(sourceInterop, legacyProvider, cancellationToken).ConfigureAwait(false);
        if (resolvedManga == null)
        {
            _logger.LogWarning("Failed to enrich metadata for provider {Provider} (SeriesId: {SeriesId}); keeping legacy details.", legacyProvider.Provider, legacyProvider.SeriesId);
        }
        else
        {
            destination.BridgeItemInfo = JsonSerializer.Serialize(resolvedManga);
            destination.MihonId = destination.MihonProviderId + "|" + resolvedManga.Url;
            destination.Provider = ps.Name;
            destination.Language = ps.Language;
            destination.Artist = string.IsNullOrWhiteSpace(resolvedManga.Artist) ? destination.Artist : resolvedManga.Artist;
            destination.Author = string.IsNullOrWhiteSpace(resolvedManga.Author) ? destination.Author : resolvedManga.Author;
            destination.Description = string.IsNullOrWhiteSpace(resolvedManga.Description) ? destination.Description : resolvedManga.Description;
            destination.Title = string.IsNullOrWhiteSpace(resolvedManga.Title) ? destination.Title : resolvedManga.Title;
            destination.ThumbnailUrl = resolvedManga.ThumbnailUrl;
            if (legacyProvider.ThumbnailUrl != null)
                state.ThumbnailLookup[legacyProvider.ThumbnailUrl] = destination.ThumbnailUrl ?? string.Empty;
        }

        return destination;
    }

    private async Task<Manga?> TryResolveMangaAsync(ISourceInterop sourceInterop, Models.SeriesProvider legacyProvider, CancellationToken cancellationToken)
    {
        try
        {
            var searchResults = await sourceInterop.SearchAsync(1, legacyProvider.Title, cancellationToken).ConfigureAwait(false);
            var selection = SelectBestManga(searchResults?.Mangas, legacyProvider.Title);
            if (selection != null)
            {
                return await sourceInterop.GetDetailsAsync(selection, cancellationToken).ConfigureAwait(false);
            }

            if (searchResults?.Mangas != null && searchResults.Mangas.Count > 1)
            {
                var fallback = SelectClosestByDistance(searchResults.Mangas, legacyProvider.Title);
                if (fallback != null)
                {
                    return await sourceInterop.GetDetailsAsync(fallback, cancellationToken).ConfigureAwait(false);
                }
            }
            _logger.LogWarning("Bridge lookup failed for provider {Provider} (SeriesId: {SeriesId}).", legacyProvider.Provider, legacyProvider.SeriesId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bridge lookup failed for provider {Provider} (SeriesId: {SeriesId}).", legacyProvider.Provider, legacyProvider.SeriesId);
        }

        return null;
    }

    private async Task<ProviderRuntimeContext> GetProviderRuntimeContextAsync(
        Models.SeriesProvider legacyProvider,
        LegacyProvider? legacy,
        MigrationState state,
        List<NewProviderStorage> providerStorages,
        Dictionary<string, ProviderRuntimeContext> runtimeContexts,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(legacyProvider.Provider) ? "Unknown" : legacyProvider.Provider;
        if (runtimeContexts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var context = new ProviderRuntimeContext(key, _mihon, providerStorages, state.ProviderPackageLookup, legacy);
        runtimeContexts[key] = context;
        return context;
    }
    /*
    private async Task<RepositoryGroup?> EnsureSeriesExtensionAsync(
        string? packageId,
        Models.SeriesProvider legacyProvider,
        MigrationState state,
        IReadOnlyCollection<TachiyomiRepository> repositories,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(packageId) && state.ExtensionGroups.TryGetValue(packageId, out var existingGroup))
        {
            return existingGroup;
        }

        (TachiyomiRepository Repository, TachiyomiExtension Extension)? match = null;

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            match = FindExtensionMatch(repositories, packageId!);
        }

        match ??= FindExtensionMatchBySource(repositories, legacyProvider.Provider, legacyProvider.Language);

        if (match == null)
        {
            _logger.LogWarning("No extension match found for provider {Provider}; series providers may be marked as unknown.", legacyProvider.Provider);
            return null;
        }

        if (state.ExtensionGroups.TryGetValue(match.Value.Extension.Package, out var cachedGroup))
        {
            return cachedGroup;
        }

        try
        {
            var group = await _mihon.AddExtensionAsync(match.Value.Repository, match.Value.Extension, false, cancellationToken)
                .ConfigureAwait(false);

            if (group != null)
            {
                state.ExtensionGroups[match.Value.Extension.Package] = group;
                state.ProviderPackageLookup[match.Value.Extension.Package] = match.Value.Extension.Package;
                if (!string.IsNullOrWhiteSpace(legacyProvider.Provider))
                {
                    state.ProviderPackageLookup[legacyProvider.Provider] = match.Value.Extension.Package;
                }

                return group;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install extension for provider {Provider}.", legacyProvider.Provider);
        }

        return null;
    }

    private static (TachiyomiRepository Repository, TachiyomiExtension Extension)? FindExtensionMatchBySource(
        IEnumerable<TachiyomiRepository> repositories,
        string? providerName,
        string? language)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        var normalizedName = NormalizeProviderName(providerName);
        var normalizedLanguage = NormalizeLanguage(language);

        var matches = repositories
            .SelectMany(repo => repo.Extensions.Select(ext => (repo, ext)))
            .Where(tuple => tuple.ext.Sources.Any(src =>
                string.Equals(src.Name, normalizedName, StringComparison.OrdinalIgnoreCase) &&
                LanguagesMatch(src.Language, normalizedLanguage)))
            .OrderByDescending(tuple => tuple.ext.VersionCode)
            .ThenByDescending(tuple => tuple.ext.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count > 0)
        {
            return matches[0];
        }

        matches = repositories
            .SelectMany(repo => repo.Extensions.Select(ext => (repo, ext)))
            .Where(tuple => tuple.ext.Sources.Any(src => string.Equals(src.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(tuple => tuple.ext.VersionCode)
            .ThenByDescending(tuple => tuple.ext.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count > 0 ? matches[0] : null;
    }
        */
    private static KaizokuBackend.Models.Chapter MapChapter(Models.Chapter chapter)
    {
        return new KaizokuBackend.Models.Chapter
        {
            Name = chapter.Name,
            Number = chapter.Number,
            ProviderUploadDate = chapter.ProviderUploadDate,
            Url = chapter.Url,
            ProviderIndex = chapter.ProviderIndex,
            DownloadDate = chapter.DownloadDate,
            ShouldDownload = chapter.ShouldDownload,
            IsDeleted = chapter.IsDeleted,
            PageCount = chapter.PageCount,
            Filename = chapter.Filename
        };
    }

    private static Manga? SelectBestManga(IReadOnlyCollection<Manga>? candidates, string? targetTitle)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(targetTitle))
        {
            var exact = candidates.FirstOrDefault(m => string.Equals(m.Title, targetTitle, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }

        return SelectClosestByDistance(candidates, targetTitle);
    }

    private static Manga? SelectClosestByDistance(IReadOnlyCollection<Manga>? candidates, string? targetTitle)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(targetTitle))
        {
            return candidates.FirstOrDefault();
        }

        var normalizedTarget = targetTitle.Trim();
        Manga? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Title))
            {
                continue;
            }

            var distance = ComputeLevenshteinDistance(candidate.Title, normalizedTarget);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best ?? candidates.FirstOrDefault();
    }

    private static int ComputeLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var matrix = new int[sourceLength + 1, targetLength + 1];

        for (var i = 0; i <= sourceLength; i++)
        {
            matrix[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            matrix[0, j] = j;
        }

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[sourceLength, targetLength];
    }

    private static string? ExtractRelativeUrl(string? fullUrl, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(fullUrl))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return fullUrl;
        }

        if (fullUrl.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return fullUrl[baseUrl.Length..].TrimStart('/');
        }

        if (Uri.TryCreate(fullUrl, UriKind.Absolute, out var parsedUri))
        {
            return parsedUri.PathAndQuery.TrimStart('/');
        }

        return fullUrl;
    }

    private static string? ResolveThumbnailUrl(string? thumbnailUrl, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return null;
        }

        if (thumbnailUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            thumbnailUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return thumbnailUrl;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return thumbnailUrl;
        }

        return string.Concat(baseUrl.TrimEnd('/'), "/", thumbnailUrl.TrimStart('/'));
    }

    private static string ResolveSeriesThumbnail(Migration.Models.Series series, MigrationState state)
    {
        string? mapped = "";
        if (!string.IsNullOrWhiteSpace(series.ThumbnailUrl))
        {
            state.ThumbnailLookup.TryGetValue(series.ThumbnailUrl, out mapped);
        }

        if (string.IsNullOrEmpty(mapped))
        {
            KaizokuBackend.Models.Database.SeriesProviderEntity? prov = state.SeriesProviders.FirstOrDefault(p => p.SeriesId == series.Id && !string.IsNullOrWhiteSpace(p.ThumbnailUrl));
            if (prov != null)
            {
                if (!prov.IsCover)
                {
                    KaizokuBackend.Models.Database.SeriesProviderEntity? prev = state.SeriesProviders.FirstOrDefault(p => p.SeriesId == series.Id && prov.IsCover);
                    if (prev != null)
                    {
                        prev.IsCover = false;
                        prov.IsCover = true;
                    }
                }
                return prov.ThumbnailUrl ?? "";
            }
        }
        return mapped ?? "";
    }

    private static string NormalizeProviderName(string? providerName)
    {
        return string.IsNullOrWhiteSpace(providerName) ? string.Empty : providerName.Trim();
    }

    private static bool LanguagesMatch(string? candidateLanguage, string normalizedLanguage)
    {
        var candidate = NormalizeLanguage(candidateLanguage);
        return candidate == normalizedLanguage || candidate == "all";
    }

    private sealed class ProviderRuntimeContext
    {
        private readonly List<NewProviderStorage> _storages = new List<NewProviderStorage>();
        private readonly Dictionary<string, string> _packageLookup;
        private readonly MihonBridgeService _mihon;

        public ProviderRuntimeContext(string key, MihonBridgeService mihon, List<NewProviderStorage> storages, Dictionary<string, string> packageLookup, LegacyProvider legacy)
        {
            Key = key;
            _storages = storages;
            Legacy = legacy;
            _packageLookup = packageLookup;
            _mihon = mihon;
        }

        public string Key { get; }
        public LegacyProvider Legacy { get; }

        private IExtensionInterop? _interop;
        public async Task<IExtensionInterop?> GetInteropAsync(CancellationToken token = default)
        {
            if (_interop == null)
            {
                _packageLookup.TryGetValue(Key, out var pkg);
                if (pkg == null)
                    return null;
                List<RepositoryGroup> grps = _mihon.ListExtensions();
                RepositoryGroup? group = grps.FirstOrDefault(g => g.Entries.Any(e => e.Extension.Package.Equals(pkg, StringComparison.OrdinalIgnoreCase)));
                if (group == null)
                    return null;
                _interop = await _mihon.GetInteropAsync(group, token).ConfigureAwait(false);
            }
            return _interop;
        }
        public NewProviderStorage? GetProviderStorage(string language)
        {
            _packageLookup.TryGetValue(Key, out var pkg);
            if (pkg == null)
                return null;
            NewProviderStorage? ret = _storages.FirstOrDefault(s => s.SourcePackageName == pkg && s.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
            if (ret == null)
                ret = _storages.FirstOrDefault(s => s.SourcePackageName == pkg && s.Language == "all");
            return ret;
        }
        public async Task<ISourceInterop?> GetSourceInteropAsync(string providerName, string language, CancellationToken token = default)
        {
            IExtensionInterop? interop = await GetInteropAsync(token).ConfigureAwait(false);
            if (interop == null)
            {
                return null;
            }

            var normalizedName = NormalizeProviderName(providerName);
            var normalizedLang = NormalizeLanguage(language);
            Mappings mapping = Legacy.Mappings.FirstOrDefault(a => a.Source.Lang.Equals(normalizedLang, StringComparison.OrdinalIgnoreCase) && a.Source.Name.Equals(normalizedName));
            if (mapping == null)
                mapping = Legacy.Mappings.FirstOrDefault(a => a.Source.Lang == "all" && a.Source.Name.Equals(normalizedName));
            if (mapping == null)
                mapping = Legacy.Mappings.FirstOrDefault(a => a.Source.Lang.Equals(normalizedLang, StringComparison.OrdinalIgnoreCase));
            if (mapping == null)
                mapping = Legacy.Mappings.FirstOrDefault(a => a.Source.Lang == "all");
            if (mapping == null)
                mapping = Legacy.Mappings.First();
            string sourceId = mapping.Source.Id;
            var candidate = interop.Sources.FirstOrDefault(s => s.Id.ToString(CultureInfo.InvariantCulture) == sourceId);
            if (candidate == null)
                candidate = interop.Sources.FirstOrDefault(a => a.Language.Equals(normalizedLang, StringComparison.OrdinalIgnoreCase) && a.Name.Equals(normalizedName));
            if (candidate == null)
                candidate = interop.Sources.FirstOrDefault(a => a.Language == "all" && a.Name.Equals(normalizedName));
            if (candidate == null)
                candidate = interop.Sources.FirstOrDefault(a => a.Language == "all");
            if (candidate == null)
                candidate = interop.Sources.First();
            return candidate;
        }

    }

    private static IReadOnlyList<string> ParseDelimited(string? value, char[] separators)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class MigrationState
    {
        public List<KaizokuBackend.Models.Database.SeriesProviderEntity> SeriesProviders { get; set; } = [];
        public IReadOnlyList<string> MihonRepositories { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Languages { get; set; } = Array.Empty<string>();
        public Dictionary<string, string> ThumbnailLookup { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RepositoryGroup> ExtensionGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ProviderPackageLookup { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

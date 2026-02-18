using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Search;
using KaizokuBackend.Services.Series;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq.Expressions;
using static System.Net.Mime.MediaTypeNames;
using Action = KaizokuBackend.Models.Database.Action;

namespace KaizokuBackend.Services.Import;

public class ImportCommandService
{
    private static readonly SemaphoreSlim _importLock = new SemaphoreSlim(1, 1);
    private readonly ILogger _logger;
    private readonly AppDbContext _db;
    private readonly SuwayomiClient _sc;
    private readonly JobHubReportService _reportingService;
    private readonly JobManagementService _jobManagementService;
    private readonly SearchQueryService _searchQuery;
    private readonly SearchCommandService _searchCommand;
    private readonly SeriesCommandService _seriesCommand;
    private readonly SeriesProviderService _seriesProvider;
    private readonly ContextProvider _baseUrl;
    private readonly ProviderCacheService _providerCache;
    private readonly ProviderInstallationService _providerInstallationService;
    private readonly ProviderQueryService _providerQueryService;
    private readonly SettingsService _settings;
    private readonly SeriesScanner _scanner;

    public ImportCommandService(
        ILogger<ImportCommandService> logger,
        SuwayomiClient sc,
        SearchQueryService searchQuery,
        SearchCommandService searchCommand,
        AppDbContext db,
        JobHubReportService reportingService,
        JobManagementService jobManagementService,
        ContextProvider baseUrl,
        SettingsService settings,
        SeriesCommandService seriesCommand,
        SeriesProviderService seriesProvider,
        ProviderCacheService providerCache,
        ProviderInstallationService providerInstallationService,
        ProviderQueryService providerQueryService,
        SeriesScanner scanner)
    {
        _logger = logger;
        _settings = settings;
        _sc = sc;
        _db = db;
        _providerInstallationService = providerInstallationService;
        _providerQueryService = providerQueryService;
        _searchQuery = searchQuery;
        _searchCommand = searchCommand;
        _reportingService = reportingService;
        _jobManagementService = jobManagementService;
        _baseUrl = baseUrl;
        _providerCache = providerCache;
        _seriesCommand = seriesCommand;
        _seriesProvider = seriesProvider;
        _scanner = scanner;
    }

    public async Task<JobResult> ScanAsync(string directoryPath, JobInfo jobInfo, CancellationToken token = default)
    {
        ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
        if ((await _jobManagementService.IsJobTypeRunningAsync(JobType.SearchProviders, token).ConfigureAwait(false))
            || (await _jobManagementService.IsJobTypeRunningAsync(JobType.InstallAdditionalExtensions, token).ConfigureAwait(false)))
        {
            _logger.LogWarning("Scan skipped: SearchProviders or InstallAdditionalExtensions job is currently running.");
            progress.Report(ProgressStatus.Completed, 100, "Scan skipped: another import job is currently running.");
            return JobResult.Success;
        }

        await _importLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<KaizokuBackend.Models.Database.Series> allseries = await _db.Series.Include(a => a.Sources).ToListAsync(token).ConfigureAwait(false);
            List<SuwayomiExtension> exts = await _sc.GetExtensionsAsync(token).ConfigureAwait(false);
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogError("Directory not found: {directoryPath}", directoryPath);
                return JobResult.Failed;
            }
            progress.Report(ProgressStatus.Started, 0, "Scanning Directories...");
            var seriesDict = new List<KaizokuInfo>();
            await _scanner.RecurseDirectoryAsync(allseries, exts, seriesDict, directoryPath, directoryPath, progress, token).ConfigureAwait(false);
            HashSet<string> folders = seriesDict.Select(a => a.Path).ToHashSet();
            await SaveImportsAsync(folders, seriesDict, token).ConfigureAwait(false);
            progress.Report(ProgressStatus.Completed, 100, "Scanning completed successfully.");
            return JobResult.Success;
        }
        finally
        {
            _importLock.Release();
        }
    }

    private async Task SaveImportsAsync(HashSet<string> existingFolders, List<KaizokuInfo> newSeries, CancellationToken token = default)
    {
        using var transaction = await _db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        try
        {
            var imports = await _db.Imports.ToListAsync(token).ConfigureAwait(false);
            foreach (KaizokuBackend.Models.Database.Import a in imports)
            {
                if (!existingFolders.Contains(a.Path, StringComparer.InvariantCultureIgnoreCase) && a.Status != ImportStatus.DoNotChange)
                {
                    _db.Imports.Remove(a);
                }
            }
            Dictionary<string, Guid> paths = await _db.GetPathsAsync(token).ConfigureAwait(false);
            foreach (KaizokuInfo k in newSeries)
            {
                KaizokuBackend.Models.Database.Series? s = null;
                if (!string.IsNullOrEmpty(k.Path) && paths.TryGetValue(k.Path, out Guid id))
                {
                    s = await _db.Series.Include(a => a.Sources)
                        .Where(a => a.Id == id)
                        .FirstOrDefaultAsync(token).ConfigureAwait(false);
                }
                bool update = false;
                bool exists = false;
                if (s != null)
                {
                    exists = true;
                    Dictionary<Chapter, SeriesProvider> chapters = s.Sources.SelectMany(a => a.Chapters, (p, c) => new { Provider = p, Chapter = c }).Where(a => !string.IsNullOrEmpty(a.Chapter.Filename)).ToDictionary(x => x.Chapter, x => x.Provider);
                    Dictionary<ArchiveInfo, ProviderInfo> archives = k.Providers.SelectMany(a => a.Archives, (p, c) => new { Provider = p, Chapter = c }).Where(a => !string.IsNullOrEmpty(a.Chapter.ArchiveName)).ToDictionary(a => a.Chapter, a => a.Provider);
                    foreach (ArchiveInfo archive in archives.Keys)
                    {
                        Chapter? c = chapters.Keys.FirstOrDefault(a => a.Filename!.Equals(archive.ArchiveName!, StringComparison.InvariantCultureIgnoreCase));
                        if (c != null)
                        {
                            chapters.Remove(c);
                        }
                        else
                        {
                            update = true;
                        }
                    }
                    if (chapters.Count > 0)
                    {
                        foreach (Chapter c in chapters.Keys)
                        {
                            _logger.LogWarning("Removing chapter '{Filename}' from provider '{Provider}' for series '{Title}' — file no longer found on disk.",
                                c.Filename, chapters[c].Provider, s.Title);
                            chapters[c].Chapters.Remove(c);
                            _db.Touch(chapters[c], c => c.Chapters);
                        }
                    }
                }
                KaizokuBackend.Models.Database.Import? import = imports.FirstOrDefault(a => a.Path.Equals(k.Path, StringComparison.InvariantCultureIgnoreCase));
                if (import != null)
                {
                    bool change = false;
                    if ((k.ArchiveCompare & ArchiveCompare.Equal) != ArchiveCompare.Equal)
                        (change, import.Info) = import.Info.Merge(k);
                    _db.Touch(import, a => a.Info);
                    if (update)
                        import.Status = ImportStatus.Import;
                    else if (!exists && import.Action != Action.Skip)
                        import.Status = ImportStatus.Import;
                    else if (import.Action == Action.Skip)
                    {
                        import.Status = ImportStatus.Skip;
                    }
                    else
                        import.Status = ImportStatus.DoNotChange;
                }
                else
                {
                    KaizokuBackend.Models.Database.Import imp = new KaizokuBackend.Models.Database.Import
                    {
                        Title = k.Title,
                        Path = k.Path,
                        Status = ImportStatus.Import,
                        Action = Action.Add,
                        Info = k
                    };
                    _db.Imports.Add(imp);
                }
            }
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(token).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<JobResult> AddExtensionsAsync(JobInfo jobInfo, int startPercentage, int maxPercentage, CancellationToken token = default)
    {
        try
        {
            ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
            if ((await _jobManagementService.IsJobTypeRunningAsync(JobType.SearchProviders, token).ConfigureAwait(false)))
            {
                _logger.LogWarning("Extension installation skipped: SearchProviders job is currently running.");
                progress.Report(ProgressStatus.Completed, maxPercentage, "Extension installation skipped: search job is running.");
                return JobResult.Success;
            }
            progress.Report(ProgressStatus.InProgress, startPercentage, null);
            List<KaizokuBackend.Models.Database.Import> imports = await _db.Imports.Where(a => a.Status == ImportStatus.Import).ToListAsync(token).ConfigureAwait(false);
            List<ProviderInfo> providerInfos = imports.SelectMany(i => i.Info.Providers).ToList();
            List<SuwayomiExtension> requiredExtensions = await _providerQueryService.GetRequiredExtensionsAsync(providerInfos, token).ConfigureAwait(false);
            if (requiredExtensions.Count > 0)
            {
                float step = (maxPercentage - startPercentage) / (float)requiredExtensions.Count;
                float acum = startPercentage;
                foreach (SuwayomiExtension ext in requiredExtensions)
                {
                    progress.Report(ProgressStatus.InProgress, (decimal)acum, ext.Name + " v" + ext.VersionCode);
                    await _providerInstallationService.InstallProviderAsync(ext.PkgName, token).ConfigureAwait(false);
                    acum += step;
                }
            }
            progress.Report(ProgressStatus.Completed, maxPercentage, "Extensions installed successfully.");
            return JobResult.Success;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error adding extensions: {Message}", e.Message);
            return JobResult.Failed;
        }
    }

    private SuwayomiSource? GetSource(ProviderInfo info, IEnumerable<SuwayomiSource> sources)
    {
        if (info.Provider == "Unknown")
            return null;
        return sources.FirstOrDefault(a => a.Name.Equals(info.Provider,  StringComparison.InvariantCultureIgnoreCase) && a.Lang.Equals(info.Language, StringComparison.InvariantCultureIgnoreCase));
    }


    public async Task UpdateImportInfoAsync(ImportInfo info, CancellationToken token = default)
    {
        KaizokuBackend.Models.Database.Import? import = await _db.Imports.FirstOrDefaultAsync(a => a.Path == info.Path, token).ConfigureAwait(false);
        if (import == null)
            return;
        import.ApplyImportInfo(info);
        _db.Touch(import, e => e.Series);
        await _db.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public async Task<JobResult> SearchSeriesAsync(JobInfo jobInfo, CancellationToken token = default)
    {
        ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
        progress.Report(ProgressStatus.Started, 0, "Starting series search...");
        await _importLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<KaizokuBackend.Models.Database.Import> imports = await _db.Imports
                .Where(a => a.Status == ImportStatus.Import)
                .ToListAsync(token).ConfigureAwait(false); ;
            if (imports.Count == 0)
            {
                progress.Report(ProgressStatus.Completed, 100, "No series to search, process complete");
                return JobResult.Success;
            }
            float step = 100 / (float)imports.Count;
            float acum = 0F;
            Dictionary<string, Guid> paths = await _db.GetPathsAsync(token).ConfigureAwait(false);
            var appSettings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            foreach (KaizokuBackend.Models.Database.Import import in imports)
            {
                try
                {
                    List<string> langs = import.Info.Providers.Select(a => a.Language).Distinct().ToList();
                    if (langs.Count == 0)
                        langs = ["en"];
                    var filteredSources = await _providerCache.GetSourcesForLanguagesAsync(langs, token).ConfigureAwait(false);
                    KaizokuBackend.Models.Database.Series? s = null;
                    if (!string.IsNullOrEmpty(import.Info.Path) && paths.TryGetValue(import.Info.Path, out Guid id))
                    {
                        s = await _db.Series.Include(a => a.Sources)
                            .Where(a => a.Id == id)
                            .FirstOrDefaultAsync(token).ConfigureAwait(false);
                    }
                    if (s != null)
                    {
                        _logger.LogInformation("Assigning '{Title}' to existing Series", import.Info.Title);
                        Dictionary<Chapter, SeriesProvider> chapters = s.Sources
                            .SelectMany(a => a.Chapters, (p, c) => new { Provider = p, Chapter = c })
                            .Where(a => !string.IsNullOrEmpty(a.Chapter.Filename))
                            .ToDictionary(x => x.Chapter, x => x.Provider);
                        Dictionary<ArchiveInfo, ProviderInfo> archives = import.Info.Providers
                            .SelectMany(a => a.Archives, (p, c) => new { Provider = p, Chapter = c })
                            .Where(a => !string.IsNullOrEmpty(a.Chapter.ArchiveName))
                            .ToDictionary(a => a.Chapter, a => a.Provider);
                        foreach (Chapter c in chapters.Keys)
                        {
                            ArchiveInfo? info = archives.Keys.FirstOrDefault(a =>
                                string.Equals(a.ArchiveName, c.Filename, StringComparison.InvariantCultureIgnoreCase));
                            if (info != null)
                                archives.Remove(info);
                        }
                        Dictionary<ProviderInfo, List<ArchiveInfo>> left = archives
                            .GroupBy(a => a.Value).ToDictionary(a => a.Key,
                                g => g.Select(b => b.Key).ToList());
                        foreach (ProviderInfo p in left.Keys.ToList())
                        {
                            if (p.Provider != "Unknown")
                            {
                                var baseQuery = s.Sources.Where(a =>
                                    a.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase));
                                if (!string.IsNullOrEmpty(p.Scanlator))
                                    baseQuery = baseQuery.Where(a =>
                                        a.Scanlator.Equals(p.Scanlator, StringComparison.InvariantCultureIgnoreCase));
                                if (!string.IsNullOrEmpty(p.Language))
                                    baseQuery = baseQuery.Where(a =>
                                        a.Language.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase));
                                SeriesProvider? existing = baseQuery.FirstOrDefault();
                                if (existing != null)
                                {
                                    existing.AssignArchives(left[p]);
                                    _db.Touch(existing, c => c.Chapters);
                                }
                                else
                                {
                                    Dictionary<SuwayomiSource, ProviderStorage> n = new();
                                    KeyValuePair<SuwayomiSource, ProviderStorage>? k = null;
                                    if (!string.IsNullOrEmpty(p.Language))
                                    {
                                        k = filteredSources.FirstOrDefault(a =>
                                            a.Key.Name.Equals(p.Provider,
                                                StringComparison.InvariantCultureIgnoreCase) &&
                                            a.Key.Lang.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase));
                                    }
                                    else
                                    {
                                        k = filteredSources.FirstOrDefault(a =>
                                            a.Key.Name.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase));
                                    }
                                    if (k != null)
                                    {
                                        n.Add(k.Value.Key, k.Value.Value);
                                        List<LinkedSeries> linked2 = await _searchQuery
                                            .SearchSeriesAsync(p.Title, n, appSettings, 0, token).ConfigureAwait(false);
                                        if (linked2.Count > 0)
                                        {
                                            AugmentedResponse augmented = await _searchCommand
                                                .AugmentSeriesAsync(linked2, token)
                                                .ConfigureAwait(false);
                                            List<FullSeries> series = augmented.Series;
                                            if (series.Count > 0)
                                            {
                                                if (!string.IsNullOrEmpty(p.Scanlator))
                                                    series = series.Where(a => a.Scanlator.Equals(p.Scanlator,
                                                        StringComparison.InvariantCultureIgnoreCase)).ToList();
                                                if (series.Count > 0)
                                                {
                                                    foreach (FullSeries f in series)
                                                    {
                                                        List<decimal?> chaps = f.Chapters.Select(a => a.Number)
                                                            .Distinct().ToList();
                                                        List<ArchiveInfo> workToDo = [];
                                                        foreach (ArchiveInfo i in left[p].ToList())
                                                        {
                                                            if (chaps.Contains(i.ChapterNumber))
                                                            {
                                                                workToDo.Add(i);
                                                                left[p].Remove(i);
                                                            }
                                                        }
                                                        if (workToDo.Count > 0)
                                                        {
                                                            SeriesProvider prov = f.CreateOrUpdate();
                                                            prov.SeriesId = s.Id;
                                                            _db.SeriesProviders.Add(prov);
                                                            s.Sources.Add(prov);
                                                            prov.AssignArchives(workToDo);
                                                        }
                                                    }
                                                    if (left.Count == 0)
                                                        left.Remove(p);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if (left.Count > 0)
                        {
                            SeriesProvider? p = s.Sources.FirstOrDefault(a => a.IsUnknown);
                            if (p == null)
                            {
                                ProviderInfo? pinfo = left.Keys.FirstOrDefault(a => a.Provider == "Unknown");
                                if (pinfo == null)
                                {
                                    pinfo = left.Keys.First();
                                    pinfo.Provider = "Unknown";
                                    pinfo.Scanlator = "";
                                }
                                p = pinfo.ToSeriesProvider();
                                p.SeriesId = s.Id;
                                _db.SeriesProviders.Add(p);
                                s.Sources.Add(p);
                                List<ArchiveInfo> arcs2 = left.SelectMany(a => a.Value).ToList();
                                p.AssignArchives(arcs2);
                            }
                        }
                        s.FillSeriesFromFullSeries(s.Sources.ToFullSeries());
                        s.Sources.CalculateContinueAfterChapter();
                        import.Status = ImportStatus.DoNotChange;
                        await _db.SaveChangesAsync(token).ConfigureAwait(false);
                        await _seriesProvider.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(s.Sources, [], token)
                            .ConfigureAwait(false);
                        await _seriesProvider.RescheduleIfNeededAsync(s.Sources, false, s.PauseDownloads, token)
                            .ConfigureAwait(false);
                    }
                    else
                    {


                        List<(ProviderInfo, SuwayomiSource, ProviderStorage)> existing = new List<(ProviderInfo, SuwayomiSource, ProviderStorage)>();
                        foreach (ProviderInfo i in import.Info.Providers)
                        {
                            SuwayomiSource? source = GetSource(i, filteredSources.Keys);
                            if (source != null)
                            {
                                existing.Add((i, source, filteredSources[source]));
                            }
                        }
                        string langstr = langs.Count == 0 ? "all" : string.Join(",", langs);
                        List<ProviderInfo> fnd = existing.Select(a => a.Item1).Distinct().ToList();
                        List<ProviderInfo> left = import.Info.Providers.Where(a => !fnd.Contains(a)).ToList();
                        List<LinkedSeries> linked = new List<LinkedSeries>();
                        if (existing.Count > 0)
                        {
                            _logger.LogInformation("Searching for '{Title}' across {Count} matched providers in languages: {langstr}", import.Info.Title, existing.Count, langstr);
                            List<LinkedSeries> list = [];
                            List<(string, SuwayomiSource, ProviderStorage)> searchlist = new List<(string, SuwayomiSource, ProviderStorage)>();
                            foreach (var n in existing)
                            {
                                if (searchlist.Any(a => a.Item1 == n.Item1.Title && a.Item2.Id == n.Item2.Id))
                                    continue; // Avoid duplicates
                                searchlist.Add((n.Item1.Title, n.Item2, n.Item3));
                            }
                            list = await _searchQuery.SearchSeriesAsync(searchlist, appSettings!, 0, token).ConfigureAwait(false);
                            Dictionary<string, List<string>> sourceTitles = new Dictionary<string, List<string>>();
                            foreach (var n in existing)
                            {
                                if (!sourceTitles.ContainsKey(n.Item2.Id))
                                    sourceTitles.Add(n.Item2.Id, new List<string>());
                                if (!sourceTitles[n.Item2.Id].Contains(n.Item1.Title))
                                    sourceTitles[n.Item2.Id].Add(n.Item1.Title);
                            }
                            if (list.Count==0)
                                left.AddRange(existing.Select(a=>a.Item1));
                
                            foreach (LinkedSeries l in list)
                            {
                                List<string> lss = sourceTitles[l.ProviderId];
                                foreach (string n in lss)
                                {
                                    if (l.Title.AreStringSimilar(n))
                                    {
                                        linked.Add(l);
                                        break;
                                    }
                                }
                            }

                        }
                        if (left.Count > 0)
                        {
                            List<SuwayomiSource> srcs = existing.Select(a => a.Item2).Distinct().ToList();
                            Dictionary<SuwayomiSource, ProviderStorage> lefts = filteredSources.Where(a => !srcs.Contains(a.Key))
                                .ToDictionary(a => a.Key, a => a.Value);
                            _logger.LogInformation("Searching for '{Title}' across {Count} providers in languages: {langstr}",
                                import.Info.Title, lefts.Count, langstr);
                            List<LinkedSeries> list = await _searchQuery
                                .SearchSeriesAsync(import.Info.Title, lefts, appSettings, 0, token)
                                .ConfigureAwait(false);
                            List<string> titles = new List<string> { import.Info.Title };
                            {
                                foreach (var n in left)
                                {
                                    bool fnda = false;
                                    foreach (string x in titles)
                                    {
                                        if (x.Equals(n.Title, StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            fnda = true;
                                            break;
                                        }
                                    }

                                    if (!fnda)
                                    {
                                        titles.Add(n.Title);
                                    }
                                }
                            }
                            foreach (LinkedSeries l in list)
                            {
                                foreach (string title in titles)
                                {
                                    if (l.Title.AreStringSimilar(title))
                                    {
                                        linked.Add(l);
                                        break;
                                    }
                                }
                            }
                        }
                        bool success = false;
                        if (linked.Count > 0)
                        {
                            AugmentedResponse augmented =
                                await _searchCommand.AugmentSeriesAsync(linked, token).ConfigureAwait(false);
                            List<FullSeries> series = augmented.Series;
                            if (series.Count > 0)
                            {
                                import.Series = series;
                                ImportInfo inf = import.ToImportInfo(_baseUrl.BaseUrl);
                                import.Action = inf.Action;
                                import.Status = inf.Status;
                                import.ContinueAfterChapter = inf.ContinueAfterChapter;
                                import.ApplyImportInfo(inf);
                                acum += step;
                                progress.Report(ProgressStatus.InProgress, (int)acum,
                                    $"{import.Info.Title} found in {string.Join(",", series.Select(a => a.Provider).Distinct())}.");
                                success = true;
                            }
                        }
                        if (!success)
                        {
                            acum += step;
                            progress.Report(ProgressStatus.InProgress, (int)acum,
                                $"Series {import.Title} not found is available providers");
                            import.Status = ImportStatus.Skip;
                            import.Action = Action.Skip;
                            _logger.LogInformation("Series '{Title}'not found", import.Info.Title);
                        }
                        await _db.SaveChangesAsync(token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching for series: {Title}", import.Info.Title);
                    import.Status = ImportStatus.Skip;
                    import.Action = Action.Skip;
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                }
            }
            progress.Report(ProgressStatus.Completed, 100, $"Search completed for {imports.Count} series");
            return JobResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during series search");
            progress.Report(ProgressStatus.Failed, 100, $"Series search failed: {ex.Message}");
            return JobResult.Failed;
        }
        finally
        {
            _importLock.Release();
        }
    }

    public async Task<JobResult> ImportSeriesAsync(JobInfo jobInfo, bool disableJob, CancellationToken token = default)
    {
        ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
        progress.Report(ProgressStatus.Started, 0, "Starting series import...");
        await _importLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<KaizokuBackend.Models.Database.Import> imports = await _db.Imports
                .Where(a => a.Status != ImportStatus.DoNotChange)
                .ToListAsync(token).ConfigureAwait(false);
            float step = 100 / (float)imports.Count;
            float acum = 0F;
            foreach (KaizokuBackend.Models.Database.Import import in imports)
            {
                if (import.Series != null && import.Series.Count > 0 && import.Action == Action.Add)
                {
                    AugmentedResponse augmented = new AugmentedResponse();
                    augmented.DisableJobs = disableJob;
                    augmented.StorageFolderPath = import.Path;
                    ImportInfo info = import.ToImportInfo(_baseUrl.BaseUrl);
                    import.ApplyImportInfo(info);
                    augmented.Series = import.Series.Where(a => a.IsSelected).ToList();
                    augmented.LocalInfo = import.Info;
                    augmented.Action = import.Action;
                    augmented.Status = import.Status;
                    Guid seriesid = await _seriesCommand.AddSeriesAsync(augmented, token).ConfigureAwait(false);
                    KaizokuBackend.Models.Database.Series? serie = await _db.Series.Include(a => a.Sources).Where(a => a.Id == seriesid).FirstOrDefaultAsync(token).ConfigureAwait(false);
                    if (serie != null)
                    {
                        KaizokuBackend.Models.Settings settings2 = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                        string finalPath = Path.Combine(settings2.StorageFolder, import.Path);
                        await serie.SaveKaizokuInfoToDirectoryAsync(finalPath, _logger, token).ConfigureAwait(false);
                    }
                }
                acum += step;
                progress.Report(ProgressStatus.InProgress, (int)acum, $"{import.Info.Title} imported.");
            }
            KaizokuBackend.Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            settings.IsWizardSetupComplete = true;
            settings.WizardSetupStepCompleted = 0;
            await _settings.SaveSettingsAsync(settings, false, token).ConfigureAwait(false);
            progress.Report(ProgressStatus.Completed, 100, $"Import completed for {imports.Count} series");
            return JobResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing series");
            progress.Report(ProgressStatus.Failed, 100, "Error importing series");
            return JobResult.Failed;
        }
        finally
        {
            _importLock.Release();
        }
    }
}

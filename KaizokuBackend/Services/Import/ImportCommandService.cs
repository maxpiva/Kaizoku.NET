using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
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
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using System.Collections.Generic;
using System.Linq.Expressions;
using static System.Net.Mime.MediaTypeNames;
using Action = KaizokuBackend.Models.Action;

namespace KaizokuBackend.Services.Import;

public class ImportCommandService
{
    private readonly ILogger _logger;
    private readonly AppDbContext _db;
    private readonly JobHubReportService _reportingService;
    private readonly JobManagementService _jobManagementService;
    private readonly SearchQueryService _searchQuery;
    private readonly SearchCommandService _searchCommand;
    private readonly SeriesCommandService _seriesCommand;
    private readonly SeriesProviderService _seriesProvider;
    private readonly ProviderCacheService _providerCache;
    private readonly ProviderManagerService _providerManagerService;
    private readonly MihonBridgeService _mihon;
    private readonly SettingsService _settings;
    private readonly SeriesScanner _scanner;

    public ImportCommandService(
        ILogger<ImportCommandService> logger,
        SearchQueryService searchQuery,
        SearchCommandService searchCommand,
        SettingsService settings,
        AppDbContext db,
        JobHubReportService reportingService,
        JobManagementService jobManagementService,
        MihonBridgeService mihon,
        SeriesCommandService seriesCommand,
        SeriesProviderService seriesProvider,
        ProviderCacheService providerCache,
        ProviderManagerService provicerManagerService,
        SeriesScanner scanner)
    {
        _logger = logger;
        _settings = settings;
        _db = db;
        _providerManagerService = provicerManagerService;
        _searchQuery = searchQuery;
        _searchCommand = searchCommand;
        _reportingService = reportingService;
        _jobManagementService = jobManagementService;
        _providerCache = providerCache;
        _seriesCommand = seriesCommand;
        _seriesProvider = seriesProvider;
        _mihon = mihon;
        _scanner = scanner;
    }

    public async Task<JobResult> ScanAsync(string directoryPath, JobInfo jobInfo, CancellationToken token = default)
    {
        ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
        if ((await _jobManagementService.IsJobTypeRunningAsync(JobType.SearchProviders, token).ConfigureAwait(false)) 
            || (await _jobManagementService.IsJobTypeRunningAsync(JobType.InstallAdditionalExtensions, token).ConfigureAwait(false)))
        {
            progress.Report(ProgressStatus.Completed, 100, "Scanning completed successfully.");
            return JobResult.Success;
        }            
  
        List<KaizokuBackend.Models.Database.SeriesEntity> allseries = await _db.Series.Include(a => a.Sources).ToListAsync(token).ConfigureAwait(false);
        List<TachiyomiRepository> repos = _mihon.ListOnlineRepositories();
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogError("Directory not found: {directoryPath}", directoryPath);
            return JobResult.Failed;
        }
        progress.Report(ProgressStatus.Started, 0, "Scanning Directories...");
        var seriesDict = new List<ImportSeriesSnapshot>();
        await _scanner.RecurseDirectoryAsync(allseries, repos, seriesDict, directoryPath, directoryPath, progress, token).ConfigureAwait(false);
        HashSet<string> folders = seriesDict.Select(a => a.Path).ToHashSet();
        await SaveImportsAsync(folders, seriesDict, token).ConfigureAwait(false);
        progress.Report(ProgressStatus.Completed, 100, "Scanning completed successfully.");
        return JobResult.Success;
    }

    private async Task ReconcileLanguagesFromImportAsync(List<KaizokuBackend.Models.Database.ImportEntity> imports)
    {
        string[] languages = imports
            .SelectMany(a => a.Info.Series.Providers.Select(b => b.Language))
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct()
            .ToArray();
        string[] avail = await _settings.GetAvailableLanguagesAsync().ConfigureAwait(false);
        languages = languages.Where(a => avail.Contains(a)).ToArray();
        SettingsDto settings = await _settings.GetSettingsAsync().ConfigureAwait(false);
        int cnt = settings.PreferredLanguages.Length;
        languages = settings.PreferredLanguages.Concat(languages.Where(a => !settings.PreferredLanguages.Contains(a))).ToArray();
        if (languages.Length != cnt)
        {
            settings.PreferredLanguages = languages;
            await _settings.SaveSettingsAsync(settings, true).ConfigureAwait(false);
        }
    }

    private async Task SaveImportsAsync(HashSet<string> existingFolders, List<ImportSeriesSnapshot> newSeries, CancellationToken token = default)
    {
        var imports = await _db.Imports.ToListAsync(token).ConfigureAwait(false);
        foreach (KaizokuBackend.Models.Database.ImportEntity a in imports)
        {
            if (!existingFolders.Contains(a.Path, StringComparer.InvariantCultureIgnoreCase) && a.Status != ImportStatus.DoNotChange)
            {
                _db.Imports.Remove(a);
            }
        }
        Dictionary<string, Guid> paths = await _db.GetPathsAsync(token).ConfigureAwait(false);
        foreach (ImportSeriesSnapshot k in newSeries)
        {
            KaizokuBackend.Models.Database.SeriesEntity? s = null;
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
                Dictionary<Chapter, SeriesProviderEntity> chapters = s.Sources.SelectMany(a => a.Chapters, (p, c) => new { Provider = p, Chapter = c }).Where(a=>!string.IsNullOrEmpty(a.Chapter.Filename)).ToDictionary(x => x.Chapter, x => x.Provider);
                Dictionary<ProviderArchiveSnapshot, ImportProviderSnapshot> archives = k.Series.Providers
                    .SelectMany(a => a.Archives, (p, c) => new { Provider = p, Chapter = c })
                    .Where(a => !string.IsNullOrEmpty(a.Chapter.ArchiveName))
                    .ToDictionary(a => a.Chapter, a => a.Provider);
                foreach (ProviderArchiveSnapshot archive in archives.Keys)
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
                        chapters[c].Chapters.Remove(c);
                        _db.Touch(chapters[c],c=>c.Chapters);
                    }
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                }
            }
            KaizokuBackend.Models.Database.ImportEntity? import = imports.FirstOrDefault(a => a.Path.Equals(k.Path, StringComparison.InvariantCultureIgnoreCase));
            if (import != null)
            {
                bool change = false;
                if ((k.ArchiveCompare & ArchiveCompare.Equal) != ArchiveCompare.Equal)
                    (change, import.Info) = import.Info.Merge(k);
                _db.Touch(import, a => a.Info);
                if (update)
                    import.Status = ImportStatus.Import;
                else if (!exists && import.Action!=Action.Skip)
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
                KaizokuBackend.Models.Database.ImportEntity imp = new KaizokuBackend.Models.Database.ImportEntity
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
    }

    public async Task<JobResult> AddExtensionsAsync(JobInfo jobInfo, int startPercentage, int maxPercentage, CancellationToken token = default)
    {
        try
        {
            ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
            if ((await _jobManagementService.IsJobTypeRunningAsync(JobType.SearchProviders, token).ConfigureAwait(false)))
            {
                progress.Report(ProgressStatus.Completed, maxPercentage, "Extensions installed successfully.");
                return JobResult.Success;
            }
            progress.Report(ProgressStatus.InProgress, startPercentage, null);
            List<KaizokuBackend.Models.Database.ImportEntity> imports = await _db.Imports.Where(a => a.Status == ImportStatus.Import).ToListAsync(token).ConfigureAwait(false);
            await ReconcileLanguagesFromImportAsync(imports).ConfigureAwait(false);
        List<ImportProviderSnapshot> importProviderSnapshots = imports.SelectMany(i => i.Info.Series.Providers).ToList();
            Dictionary<TachiyomiExtension, TachiyomiRepository> requiredExtensions = await _providerManagerService.GetRequiredExtensionsAsync(importProviderSnapshots, token).ConfigureAwait(false);
            if (requiredExtensions.Count > 0)
            {
                float step = (maxPercentage - startPercentage) / (float)requiredExtensions.Count;
                float acum = startPercentage;
                foreach ((TachiyomiExtension text, TachiyomiRepository trepo) in requiredExtensions)
                {
                    progress.Report(ProgressStatus.InProgress, (decimal)acum, text.ParsedName() + " " + text.Version);
                    await _providerManagerService.InstallProviderAsync(text.Package, trepo.Name, false, token).ConfigureAwait(false);
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

    private ProviderStorageEntity? GetSource(ImportProviderSnapshot info, IEnumerable<ProviderStorageEntity> sources)
    {
        if (info.Provider == "Unknown")
            return null;
        if (string.IsNullOrEmpty(info.Language))
        {
            List<ProviderStorageEntity> filtered2 = sources.Where(a => a.Name.Equals(info.Provider, StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (filtered2.Count>0)
            {
                ProviderStorageEntity? ps = filtered2.FirstOrDefault(a => a.Language.Equals("all", StringComparison.InvariantCultureIgnoreCase));
                if (ps==null)
                    ps = filtered2.First();
            }
            return null;
        }
        List<ProviderStorageEntity> filtered = sources.Where(a => a.Name.Equals(info.Provider, StringComparison.InvariantCultureIgnoreCase) && a.Language.Equals(info.Language, StringComparison.InvariantCultureIgnoreCase)).ToList();
        if (filtered.Count == 0)
        {
            filtered = sources.Where(a => a.Name.Equals(info.Provider, StringComparison.InvariantCultureIgnoreCase) && a.Language.Equals("all", StringComparison.InvariantCultureIgnoreCase)).ToList();
        }
        if (filtered.Count > 0)
            return filtered.First();
        return null;
    }


    public async Task UpdateImportSeriesEntryAsync(ImportSeriesEntry info, CancellationToken token = default)
    {
        KaizokuBackend.Models.Database.ImportEntity? import = await _db.Imports.FirstOrDefaultAsync(a => a.Path == info.Path, token).ConfigureAwait(false);
        if (import == null)
            return;
        import.ApplyImportSeriesEntry(info);
        _db.Touch(import, e => e.Series);
        await _db.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public async Task<JobResult> SearchSeriesAsync(JobInfo jobInfo, CancellationToken token = default)
    {
        ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
        progress.Report(ProgressStatus.Started, 0, "Starting series search...");
        try
        {
            List<KaizokuBackend.Models.Database.ImportEntity> imports = await _db.Imports
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
            List<RepositoryGroup> local_repo = _mihon.ListExtensions();
            foreach (KaizokuBackend.Models.Database.ImportEntity import in imports)
            {
                try
                {
                    List<string> langs = import.Info.Series.Providers.Select(a => a.Language).Distinct().ToList();
                    if (langs.Count == 0)
                        langs = ["en"];
                    var filteredSources = await _providerCache.GetSourcesForLanguagesAsync(langs, token).ConfigureAwait(false);
                    KaizokuBackend.Models.Database.SeriesEntity? s = null;
                    if (!string.IsNullOrEmpty(import.Info.Path) && paths.TryGetValue(import.Info.Path, out Guid id))
                    {
                        s = await _db.Series.Include(a => a.Sources)
                            .Where(a => a.Id == id)
                            .FirstOrDefaultAsync(token).ConfigureAwait(false);
                    }
                    if (s != null)
                    {
                        _logger.LogInformation("Assigning '{Title}' to existing Series", import.Info.Title);
                        Dictionary<Chapter, SeriesProviderEntity> chapters = s.Sources
                            .SelectMany(a => a.Chapters, (p, c) => new { Provider = p, Chapter = c })
                            .Where(a => !string.IsNullOrEmpty(a.Chapter.Filename))
                            .ToDictionary(x => x.Chapter, x => x.Provider);
                        Dictionary<ProviderArchiveSnapshot, ImportProviderSnapshot> archives = import.Info.Series.Providers
                            .SelectMany(a => a.Archives, (p, c) => new { Provider = p, Chapter = c })
                            .Where(a => !string.IsNullOrEmpty(a.Chapter.ArchiveName))
                            .ToDictionary(a => a.Chapter, a => a.Provider);
                        foreach (Chapter c in chapters.Keys)
                        {
                            ProviderArchiveSnapshot? info = archives.Keys.FirstOrDefault(a =>
                                string.Equals(a.ArchiveName, c.Filename, StringComparison.InvariantCultureIgnoreCase));
                            if (info != null)
                                archives.Remove(info);
                        }
                        Dictionary<ImportProviderSnapshot, List<ProviderArchiveSnapshot>> left = archives
                            .GroupBy(a => a.Value).ToDictionary(a => a.Key,
                                g => g.Select(b => b.Key).ToList());
                        foreach (ImportProviderSnapshot p in left.Keys.ToList())
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
                                SeriesProviderEntity? existing = baseQuery.FirstOrDefault();
                                if (existing != null)
                                {
                                    existing.AssignArchives(left[p]);
                                    _db.Touch(existing, c => c.Chapters);
                                }
                                else
                                {
                                    ProviderStorageEntity k = filteredSources.BestMatch(local_repo, p.Provider, p.Language);
                                    if (k != null)
                                    {
                                        List<LinkedSeriesDto> linked2 = await _searchQuery
                                            .SearchSeriesAsync(p.Title, new List<ProviderStorageEntity> { k }, appSettings, 0, token).ConfigureAwait(false);
                                        if (linked2.Count > 0)
                                        {
                                            AugmentedResponseDto augmented = await _searchCommand
                                                .AugmentSeriesAsync(linked2, token)
                                                .ConfigureAwait(false);
                                            List<ProviderSeriesDetails> series = augmented.Series;
                                            if (series.Count > 0)
                                            {
                                                if (!string.IsNullOrEmpty(p.Scanlator))
                                                    series = series.Where(a => a.Scanlator.Equals(p.Scanlator,
                                                        StringComparison.InvariantCultureIgnoreCase)).ToList();
                                                if (series.Count > 0)
                                                {
                                                    foreach (ProviderSeriesDetails f in series)
                                                    {
                                                        List<decimal?> chaps = f.Chapters.Select(a => a.Number)
                                                            .Distinct().ToList();
                                                        List<ProviderArchiveSnapshot> workToDo = [];
                                                        foreach (ProviderArchiveSnapshot i in left[p].ToList())
                                                        {
                                                            if (chaps.Contains(i.ChapterNumber))
                                                            {
                                                                workToDo.Add(i);
                                                                left[p].Remove(i);
                                                            }
                                                        }
                                                        if (workToDo.Count > 0)
                                                        {
                                                            SeriesProviderEntity prov = f.CreateOrUpdate();
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
                            SeriesProviderEntity? p = s.Sources.FirstOrDefault(a => a.IsUnknown);
                            if (p == null)
                            {
                                ImportProviderSnapshot? pinfo = left.Keys.FirstOrDefault(a => a.Provider == "Unknown");
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
                                List<ProviderArchiveSnapshot> arcs2 = left.SelectMany(a => a.Value).ToList();
                                p.AssignArchives(arcs2);
                            }
                        }
                        s.FillSeriesFromProviderSeriesDetails(s.Sources.ToProviderSeriesDetails(),null);
                        s.Sources.CalculateContinueAfterChapter(null);
                        import.Status = ImportStatus.DoNotChange;
                        await _db.SaveChangesAsync(token).ConfigureAwait(false);
                        await _seriesProvider.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(s.Sources, [], token)
                            .ConfigureAwait(false);
                        await _seriesProvider.RescheduleIfNeededAsync(s.Sources, false, s.PauseDownloads, token)
                            .ConfigureAwait(false);
                    }
                    else
                    {


                        List<(ImportProviderSnapshot pinfo, ProviderStorageEntity ps)> existing = new List<(ImportProviderSnapshot, ProviderStorageEntity)>();
                        foreach (ImportProviderSnapshot i in import.Info.Series.Providers)
                        {
                            ProviderStorageEntity? ps = GetSource(i, filteredSources);
                            if (ps!=null)
                            {
                                existing.Add((i, ps));
                            }
                        }
                        string langstr = langs.Count == 0 ? "all" : string.Join(",", langs);
                        List<ImportProviderSnapshot> fnd = existing.Select(a => a.Item1).Distinct().ToList();
                        List<ImportProviderSnapshot> left = import.Info.Series.Providers.Where(a => !fnd.Contains(a)).ToList();
                        List<LinkedSeriesDto> linked = new List<LinkedSeriesDto>();
                        if (existing.Count > 0)
                        {
                            _logger.LogInformation("Searching for '{Title}' across {Count} matched providers in languages: {langstr}", import.Info.Title, existing.Count, langstr);
                            List<LinkedSeriesDto> list = [];
                            List<(string keyword, ProviderStorageEntity pstorage)> searchlist = new List<(string keyword, ProviderStorageEntity pstorage)>();
                            foreach (var n in existing)
                            {
                                if (searchlist.Any(a => a.keyword == n.pinfo.Title && a.pstorage.MihonProviderId == n.ps.MihonProviderId))
                                    continue; // Avoid duplicates
                                searchlist.Add((n.pinfo.Title, n.ps));
                            }
                            list = await _searchQuery.SearchSeriesAsync(searchlist, appSettings!, 0, token).ConfigureAwait(false);
                            Dictionary<string, List<string>> sourceTitles = new Dictionary<string, List<string>>();
                            foreach (var n in existing)
                            {
                                if (!sourceTitles.ContainsKey(n.ps.MihonProviderId))
                                    sourceTitles.Add(n.ps.MihonProviderId, new List<string>());
                                if (!sourceTitles[n.ps.MihonProviderId].Contains(n.pinfo.Title))
                                    sourceTitles[n.ps.MihonProviderId].Add(n.pinfo.Title);
                            }
                            if (list.Count==0)
                                left.AddRange(existing.Select(a=>a.Item1));
                
                            foreach (LinkedSeriesDto l in list)
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
                            List<string> srcs = existing.Select(a => a.ps.MihonProviderId).Distinct().ToList();
                            List<ProviderStorageEntity> lefts = filteredSources.Where(a => !srcs.Contains(a.MihonProviderId))
                                .ToList();
                            _logger.LogInformation("Searching for '{Title}' across {Count} providers in languages: {langstr}",
                                import.Info.Title, lefts.Count, langstr);
                            List<LinkedSeriesDto> list = await _searchQuery
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
                            foreach (LinkedSeriesDto l in list)
                            {
                                foreach (string title in titles)
                                {
                                    if (l.Title.AreStringSimilar(title,0.1))
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
                            AugmentedResponseDto augmented =
                                await _searchCommand.AugmentSeriesAsync(linked, token).ConfigureAwait(false);
                            List<ProviderSeriesDetails> series = augmented.Series;
                            if (series.Count > 0)
                            {
                                import.Series = series;
                                ImportSeriesEntry inf = import.ToImportSeriesEntry();
                                import.Action = inf.Action;
                                import.Status = inf.Status;
                                import.ContinueAfterChapter = inf.ContinueAfterChapter;
                                import.ApplyImportSeriesEntry(inf);
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
    }

    public async Task<JobResult> ImportSeriesAsync(JobInfo jobInfo, bool disableJob, CancellationToken token = default)
    {
        ProgressReporter progress = _reportingService.CreateReporter(jobInfo);
        progress.Report(ProgressStatus.Started, 0, "Starting series import...");
        List<KaizokuBackend.Models.Database.ImportEntity> imports = await _db.Imports
            .Where(a => a.Status != ImportStatus.DoNotChange)
            .AsNoTracking()
            .ToListAsync(token).ConfigureAwait(false);
        float step = 100 / (float)imports.Count;
        float acum = 0F;
        try
        {
            foreach (KaizokuBackend.Models.Database.ImportEntity import in imports)
            {
                if (import.Series != null && import.Series.Count > 0 && import.Action == Action.Add)
                {
                    AugmentedResponseDto augmented = new AugmentedResponseDto();
                    augmented.DisableJobs = disableJob;
                    augmented.StorageFolderPath = import.Path;
                    ImportSeriesEntry info = import.ToImportSeriesEntry();
                    import.ApplyImportSeriesEntry(info);
                    augmented.Series = import.Series.Where(a => a.IsSelected).ToList();
                    augmented.LocalInfo = import.Info;
                    augmented.Action = import.Action;
                    augmented.Status = import.Status;
                    Guid seriesid = await _seriesCommand.AddSeriesAsync(augmented, token).ConfigureAwait(false);
                    KaizokuBackend.Models.Database.SeriesEntity? serie = await _db.Series.Include(a => a.Sources).Where(a => a.Id == seriesid).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
                    if (serie != null)
                    {
                        SettingsDto settings2 = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                        string finalPath = Path.Combine(settings2.StorageFolder, import.Path);
                        await serie.SaveImportSeriesSnapshotToDirectoryAsync(finalPath, _logger, token).ConfigureAwait(false);
                    }
                }
                acum += step;
                progress.Report(ProgressStatus.InProgress, (int)acum, $"{import.Info.Title} imported.");
            }
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
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
    }
}


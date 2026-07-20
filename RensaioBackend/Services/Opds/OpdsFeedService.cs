using com.sun.tools.@internal.xjc;
using javax.activation;
using javax.xml.crypto;
using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Migration.Models;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Opds;
using RensaioBackend.Models.ReadState;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models.Extensions;
using sun.security.provider;
using System.Text;
using System.Text.Json;
using System.Xml;
using static com.sun.tools.@internal.xjc.reader.xmlschema.bindinfo.BIConversion;

namespace RensaioBackend.Services.Opds;

/// <summary>
/// Service for building OPDS (Open Publication Distribution System) Atom XML feeds.
/// Supports per-user read-state tracking via rensaio.json.
/// </summary>
public class OpdsFeedService
{
    private readonly AppDbContext _db;
    private readonly ReadStateService _readStateService;
    private readonly ThumbCacheService _thumbCache;
    private readonly SettingsService _settingsService;
    private readonly IConfiguration _configuration;

    public OpdsFeedService(AppDbContext db, ReadStateService readStateService,
        ThumbCacheService thumbCache, SettingsService settingsService, IConfiguration configuration)
    {
        _db = db;
        _readStateService = readStateService;
        _thumbCache = thumbCache;
        _settingsService = settingsService;
        _configuration = configuration;
    }

    /// <summary>
    /// Whether the title text should include read-state suffixes (✓, page progress).
    /// When false (default), titles are clean — clients rely on <category> and p5:lastRead instead.
    /// </summary>
    private bool FallbackReadStateEnabled => _configuration.GetValue<bool>("FallbackReadState");


    public async Task<(Stream, string mimeType)?> GetChapterStreamAsync(Guid seriesId, string filename,CancellationToken token = default)
    {
        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(a => a.Id == seriesId, token);
        if (series == null)
            return null;
        string? archiveName = _settingsService.DirectSettings?.ResolveChapterPath(series.StoragePath, filename);
        if (archiveName == null)
            return null;
        string mimeType = GetChapterContentType(filename);
        return (File.OpenRead(archiveName), mimeType);
    }

    private StringBuilder RenderHeader(UserEntity user, string id, string title, string user_agent)
    {
        StringBuilder sb = new();
        List<string> ids = new List<string> { "rensaio", user.OpdsPath };
        ids.AddRange(id.Split(":"));
        string newId = string.Join(':', ids.Select(a => EscapeXml(a)));
        title = EscapeXml(title);
        sb.AppendLine($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns=""http://www.w3.org/2005/Atom"" xmlns:opds=""http://opds-spec.org/2010/catalog"">
  <generator uri=""https://www.rensaio.net"" version=""1.0"">Rensaiō OPDS Server</generator>
  <updated>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</updated>
  <id>{newId}</id>
  <title>{title}</title>");
        return sb;
    }
    private void RenderFooter(StringBuilder sb)
    {
        sb.Append("</feed>");
    }

    private async Task RenderFolderAsync(StringBuilder sb, UserEntity user, string id, string title, List<SeriesEntity> thumbsSource, DateTime? date = null,  bool first=false, CancellationToken token = default)
    {
        string thumb = first ? await PickFirstThumbnailAsync(user.OpdsPath, thumbsSource, token).ConfigureAwait(false) : await PickRandomThumbnailAsync(user.OpdsPath, thumbsSource, token).ConfigureAwait(false);
        RenderFolder(sb, user, id, title, thumb, date);
    }



    private async void RenderFolder(StringBuilder sb, UserEntity user, string id, string title, string thumb, DateTime? date = null)
    {
        List<string> ids = new List<string> { "rensaio", user.OpdsPath };
        List<string> paths = new List<string> { user.OpdsPath };
        ids.AddRange(id.Split(":"));
        paths.AddRange(id.Split(":"));
        string newId = string.Join(':', ids.Select(a => EscapeXml(a)));
        string path = EscapeXml("/" +string.Join('/', paths.Select(a => Uri.EscapeDataString(a))));
        title = EscapeXml(title);

        sb.AppendLine($@"  <entry>
    <id>{newId}</id>
    <title>{title}</title>
    <link rel=""subsection"" href=""{path}""/>
  {thumb}
    <updated>{date?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")}</updated>
  </entry>");
    }
    private async void RenderSeriesFolder(StringBuilder sb, SeriesEntity series, UserEntity user, string id, string title, string thumb, DateTime? date = null)
    {
        List<string> ids = new List<string> { "rensaio", user.OpdsPath };
        List<string> paths = new List<string> { user.OpdsPath };
        ids.AddRange(id.Split(":"));
        paths.AddRange(id.Split(":"));
        string newId = string.Join(':', ids.Select(a => EscapeXml(a)));
        string path = EscapeXml("/" + string.Join('/', paths.Select(a => Uri.EscapeDataString(a))));
        title = EscapeXml(title);

        sb.AppendLine($@"  <entry>
    <id>{newId}</id>
    <title>{title}</title>
    <link rel=""subsection"" href=""{path}""/>
  {thumb}");
        HashSet<string> renderedChapterIds = new HashSet<string>();
        HashSet<string> used = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        RenderAuthors(sb, series);
        RenderArtists(sb, series);
        RenderSummary(sb, series);
        RenderCategories(sb, series, used);
        RenderTags(sb, series, used);
        sb.AppendLine($@"    <updated>{date?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")}</updated>
  </entry>");
    }
    /// <summary>
    /// Builds the root OPDS catalog feed for a user.
    /// Each folder entry gets a thumbnail from a random series within that folder.
    /// The Categories entry is only shown when categories are configured in settings.
    /// </summary>
    public async Task<string> BuildRootCatalogAsync(UserEntity user, string user_agent, CancellationToken token = default)
    {
        var allSeries = await _db.Series.Include(a=>a.Sources).AsNoTracking().OrderBy(a=>a.Title).ToListAsync(token);
        var categories = _settingsService.DirectSettings?.Categories ?? [];
        var readingSeries = FilterByReading(allSeries, user.Username);
        var lastSeries = FilterByLast(allSeries);
        string allThumb = await PickRandomThumbnailAsync(user.OpdsPath, allSeries, token);
        var sb = RenderHeader(user, "", $"Rensaiō - {EscapeXml(user.Username)}'s Library", user_agent);
        if (readingSeries.Count > 0)
            await RenderFolderAsync(sb, user, "reading", "Reading", readingSeries, null, true, token).ConfigureAwait(false);
        if (lastSeries.Count > 0)
            await RenderFolderAsync(sb, user, "last-changed", "Last Changed", lastSeries, null, true, token).ConfigureAwait(false);
        await RenderFolderAsync(sb, user, "all-series", "All Series", allSeries, null, false, token).ConfigureAwait(false);
        if (categories.Length > 0)
            await RenderFolderAsync(sb, user, "categories","Categories", allSeries, null, false, token).ConfigureAwait(false);
        await RenderFolderAsync(sb, user, "tags", "Tags", allSeries, null, false, token).ConfigureAwait(false);
        await RenderFolderAsync(sb, user, "sources", "Sources", allSeries, null, false, token).ConfigureAwait(false);
        RenderFooter(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Builds the "Reading" folder feed showing series with unread chapters.
    /// /// <summary>
    /// Builds the "Reading" folder feed showing series with unread chapters.
    /// </summary>
    /*
    public async Task<string> BuildReadingFeedAsync(UserEntity user, string user_agent, CancellationToken token = default)
    {
        var seriesList = await _db.Series
            .Include(s => s.Sources)
            .AsNoTracking()
            .ToListAsync(token);

        var sb = new StringBuilder();
        BuildHeader(sb, $"rensaio:{EscapeXml(user.OpdsPath)}:reading", $"Reading - {EscapeXml(user.Username)}", user_agent);
        seriesList = FilterByReading(seriesList, user.Username).OrderByDescending(a => a.Sources.Max(s => s.Chapters.Max(c=>c.DownloadDate ?? DateTime.MinValue))).ToList();
        
        foreach (var series in seriesList.Where(s => s.Sources.Any(sp => !sp.IsUnknown)))
        {
            int totalChapters = series.ChapterCount;
            int unreadCount = _readStateService.GetUnreadChaptersCount(user.Username, series.StoragePath ?? "", totalChapters);
            string statusSuffix = unreadCount > 0 ? $" [{unreadCount}]" : "";
            string thumbnailLinks = await BuildThumbnailLinkAsync(user.OpdsPath, series.ThumbnailUrl, token);

            sb.Append($@"  <entry>
    <title>{EscapeXml(series.Title)}{EscapeXml(statusSuffix)}</title>
    <id>rensaio:series:{series.Id}</id>
  {thumbnailLinks}
    <link rel=""subsection"" href=""/{EscapeXml(user.OpdsPath)}/series/{series.Id}""/>
    <updated>{series.LastChapterDate?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")}</updated>
  </entry>
");
        }

        sb.Append("</feed>");
        return sb.ToString();
    }
    */
    /// <summary>
    /// Builds the "All Series" feed.
    /// </summary>
    public async Task<string> BuildSeriesFeedAsync(UserEntity user, string id, string title, string user_agent, string? category = null, string? tag = null, string? source = null, bool filterReading = false, bool last = false, CancellationToken token = default)
    {
        var seriesList = await _db.Series
            .Include(s => s.Sources)
            .AsNoTracking().OrderBy(a=>a.Title)
            .ToListAsync(token);

        var sb = RenderHeader(user, id, title, user_agent);
        List<SeriesEntity> serlist = seriesList;
        if (category!=null)
            serlist = FilterByCategory(serlist, category).OrderBy(a => a.Title).ToList();
        if (tag!=null)
            serlist = FilterByTag(serlist, tag).OrderBy(a => a.Title).ToList();
        if (source != null)
            serlist = FilterBySource(serlist, source).OrderBy(a=>a.Title).ToList();
        if (filterReading)
            serlist = FilterByReading(serlist, user.Username).ToList();
        if (last)
            serlist = FilterByLast(serlist).OrderByDescending(a => a.Sources.Max(s => s.Chapters.Max(c => c.DownloadDate ?? DateTime.MinValue))).ToList();
        foreach (var series in serlist)
        {
            int totalChapters = series.ChapterCount;
            int cnt = series.ChapterCount;
            if (filterReading || last)
                cnt = _readStateService.GetUnreadChaptersCount(user.Username, series.StoragePath ?? "", totalChapters);
            string stitle = $"{series.Title}{(cnt > 0 ? $" [{cnt}]" : "")}";
            string thumbnailLinks = await BuildThumbnailLinkAsync(user.OpdsPath, series.ThumbnailUrl, token);
            RenderSeriesFolder(sb, series, user, $"series:{series.Id}", stitle, thumbnailLinks, series.LastChapterDate);
        }
        RenderFooter(sb);
        return sb.ToString();
    }



    internal async Task<(Models.Chapter Chapter, SeriesProviderEntity Provider)?> FindChapterInSeriesAsync(Guid seriesId, string filename, CancellationToken token = default)
    {
        var providers = await _db.SeriesProviders.Where(a => a.SeriesId == seriesId).ToListAsync(token);
        foreach(var provider in providers)
        {
            var chapter = provider.Chapters.FirstOrDefault(a => a.Filename == filename);
            if (chapter != null)
                return (chapter, provider);
        }
        return null;
    }
    private void RenderAuthors(StringBuilder sb, SeriesEntity series)
    {
        var authors = series.Author?.Split(",").Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).ToList() ?? [];
        foreach (var author in authors)
        {
            sb.AppendLine($"    <author><name>{EscapeXml(author)}</name></author>");
        }
    }
    private void RenderArtists(StringBuilder sb, SeriesEntity series)
    {
        var artists = series.Artist?.Split(",").Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).ToList() ?? [];
        foreach (var artist in artists)
        {
            sb.AppendLine($"    <artist><name>{EscapeXml(artist)}</name></artist>");
        }
    }
    private void RenderSummary(StringBuilder sb, SeriesEntity series)
    {
        if (!string.IsNullOrEmpty(series.Description))
        {
            sb.AppendLine($"    <summary>{EscapeXml(series.Description)}</summary>");
        }
    }
    private void RenderCategories(StringBuilder sb, SeriesEntity series, HashSet<string> used)
    {
        string category = FindCategory(series);
        if (!string.IsNullOrEmpty(category) && !used.Contains(category))
        {
            sb.AppendLine($@"    <category term=""{EscapeXml(category)}"" scheme=""http://opds-spec.org/state"" label=""{EscapeXml(category)}""/>");
            used.Add(category);
        }
    }
    private void RenderTags(StringBuilder sb, SeriesEntity series, HashSet<string> used)
    {
        foreach (var tag in series.Genre?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>())
        {
            if (!used.Contains(tag))
            {
                sb.AppendLine($@"    <category term=""{EscapeXml(tag)}"" scheme=""http://opds-spec.org/state"" label=""{EscapeXml(tag)}""/>");
                used.Add(tag);
            }
        }
    }
    internal async Task<string> BuildChapterEntryAsync(UserEntity user, Guid seriesId, string language, SeriesEntity series, SeriesProviderEntity provider, Models.Chapter chapter, List<ChapterReadState> readStates, string chapterTitle, bool multiSource, CancellationToken token = default)
    {
        StringBuilder sb = new StringBuilder();
        string progressionBaseHref = $"/{EscapeXml(user.OpdsPath)}/progression/{seriesId}/";

        string base64Filename = EncodeBase64Url(chapter.Filename);
        string chapterId = $"{seriesId}:language:{language}:{base64Filename}";
 
        int pageCount = chapter.PageCount ?? 0;

        // Determine read state for display + category hints
        var chapterState = readStates.FirstOrDefault(rs => rs.ChapterNumber == chapter.ChapterNumber);
        bool isCompleted = chapterState?.IsCompleted ?? false;
        bool isPartial = chapterState != null && !isCompleted && chapterState.Progress > 0;

        // Progression data for the OPDS Progression 1.0 link
        string progressionHref = progressionBaseHref + EscapeXml(base64Filename);
        string progValueStr;
        string progModifiedStr;
        string progDeviceId;
        string progDeviceName;
        if (chapterState != null)
        {
            progValueStr = chapterState.Progress.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            progModifiedStr = chapterState.LastReadAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            progDeviceId = string.IsNullOrEmpty(chapterState.LastReadDeviceId) ? EscapeXml($"urn:rensaio:user:{EscapeXml(user.Username)}") : EscapeXml(chapterState.LastReadDeviceId);
            progDeviceName = string.IsNullOrEmpty(chapterState.LastReadDeviceName) ? "Rensaiō" : EscapeXml(chapterState.LastReadDeviceName);
        }
        else
        {
            progValueStr = "0.0000";
            progModifiedStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            progDeviceId = EscapeXml($"urn:rensaio:user:{EscapeXml(user.Username)}");
            progDeviceName = "Rensaiō";
        }
        int lastReadPage = pageCount > 0
            ? (int)Math.Round((chapterState?.Progress ?? 0) * pageCount)
            : 0;
        int totalPages = pageCount;

        string readingStateHref = $"/{EscapeXml(user.OpdsPath)}/reading-state/{seriesId}/{EscapeXml(base64Filename)}";

        // Build chapter title: Series Name + optional chapter number
        // If only 1 chapter total, omit number (e.g. "One Piece" instead of "One Piece 1")
        // If multiple sources, suffix with [ProviderName]


        if (multiSource)
        {
            string providerName = !string.IsNullOrWhiteSpace(provider.Provider)
                ? provider.Provider
                : "Unknown";
            chapterTitle += $" [{EscapeXml(providerName)}]";
        }

        // Optional read-state suffix — only when FallbackReadState is enabled (default: off).
        // Modern OPDS clients (Panels, Chunky) rely on <category> and p5:lastRead instead.
        string titleSuffix = "";
        if (FallbackReadStateEnabled)
        {
            if (isCompleted)
                titleSuffix = " ✓";
            else if (isPartial)
                titleSuffix = $" ({lastReadPage}/{totalPages})";
        }

        string thumbnailLinks = await BuildThumbnailLinkAsync(user.OpdsPath, series.ThumbnailUrl, token);

        sb.Append($@"  <entry>
    <title>{EscapeXml(chapterTitle)}{EscapeXml(titleSuffix)}</title>
    <id>rensaio:{EscapeXml(user.OpdsPath)}:{EscapeXml(chapterId)}</id>
  {thumbnailLinks}
    <link rel=""http://opds-spec.org/progression"" type=""application/opds-progression+json"" href=""{EscapeXml(progressionHref)}""");
        if (chapterState != null)
        {
            sb.AppendLine($@">
        <properties>
        <progression progression=""{progValueStr}"" modified=""{progModifiedStr}"">
            <device id=""{progDeviceId}"" name=""{progDeviceName}""/>
            <references/>
        </progression>
        </properties>
    </link>");
        }
        else
            sb.AppendLine("/>");


        // Page Streaming Extension: single stream link with p5:count, p5:lastRead, p5:lastReadDate
        // Per OPDS-PSE 1.1 & 1.2 specs: https://github.com/anansi-project/opds-pse


        string pseAttrs = string.Empty;
        if (pageCount > 0)
        {
            pseAttrs = $@"p5:count=""{pageCount}""";
            if (chapterState != null && lastReadPage > 0)
            {
                pseAttrs += $@" p5:lastRead=""{lastReadPage}""";
                pseAttrs += $@" p5:lastReadDate=""{chapterState.LastReadAt:yyyy-MM-ddTHH:mm:ssZ}""";
            }
        }
        string streamHref = $"/{EscapeXml(user.OpdsPath)}/image/{seriesId}/{EscapeXml(base64Filename)}/{{pageNumber}}";
        string acqHref = $"/{EscapeXml(user.OpdsPath)}/series/{seriesId}/chapter/{EscapeXml(base64Filename)}";
        string icontentType = "image/jpeg"; //TBD        
        string contentType = GetChapterContentType(chapter.Filename);

        sb.AppendLine($@"    <link xmlns:p5=""http://vaemendis.net/opds-pse/ns"" rel=""http://opds-spec.org/acquisition/open-access"" type=""{EscapeXml(contentType)}"" href=""{EscapeXml(acqHref)}"" {pseAttrs} status=""ready"" title=""{EscapeXml(chapter.Filename ?? "")}""/>
    <link xmlns:p5=""http://vaemendis.net/opds-pse/ns"" rel=""http://vaemendis.net/opds-pse/stream"" type=""{EscapeXml(icontentType)}"" href=""{EscapeXml(streamHref)}"" {pseAttrs}/>
    <updated>{chapter.DownloadDate?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")}</updated>");


        HashSet<string> used = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        RenderAuthors(sb, series);
        RenderArtists(sb, series);

        // Inline read state category — standard OPDS 1.2 catalog convention
        // Used by Komga, Kavita, Panels, Chunky to show read/unread inline
        if (isCompleted)
        {
            sb.AppendLine(@"    <category term=""read"" scheme=""http://opds-spec.org/state"" label=""Read""/>");
            used.Add("read");
        }
        else if (isPartial)
        {
            sb.AppendLine(@"    <category term=""reading"" scheme=""http://opds-spec.org/state"" label=""Reading""/>");
            used.Add("reading");
        }
        else
        {
            sb.AppendLine(@"    <category term=""unread"" scheme=""http://opds-spec.org/state"" label=""Unread""/>");
            used.Add("unread");
        }
        RenderCategories(sb, series, used);
        sb.AppendLine("  </entry>");

        return sb.ToString();
    }


    /*
    <link rel=""http://opds-spec.org/reading-state"" type=""application/json"" href=""{EscapeXml(readingStateHref)}""/>
    <link rel=""http://opds-spec.org/book/progress"" type=""application/json"" href=""{EscapeXml(readingStateHref)}""/>
    */
    /// <summary>
    /// Builds a chapter list feed for a specific series.
    /// If the series has multiple languages, returns a language selection feed instead.
    /// </summary>
    public async Task<string> BuildSeriesFeedAsync(UserEntity user, string user_agent, Guid seriesId, string? language = null, CancellationToken token = default)
    {
        var series = await _db.Series
            .Include(s => s.Sources)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == seriesId, token);

        if (series == null)
            return BuildErrorFeed("Series not found");

        var providers = series.Sources.ToList();
        foreach(var p in providers)
        {
            if (string.IsNullOrEmpty(p.Language))
            {
                p.Language = "en";
            }
        }
        // If no language specified, check if we need language selection
        if (language == null)
        {
            var languages = providers.Select(p => p.Language).Distinct().ToList();
            if (languages.Count > 1)
            {
                return await BuildLanguageSelectionFeedAsync(user, user_agent, series, languages, token).ConfigureAwait(false);
            }
            language = languages.FirstOrDefault() ?? "en";
        }

        // Get filtered providers for this language
        var langProviders = providers.Where(p => p.Language == language).ToList();
        if (langProviders.Count == 0)
            return BuildErrorFeed("No chapters found for this language");

        // Collect deduplicated chapters (only those backed by actual files)
        var allChapters = new List<Models.Chapter>();
        foreach (var p in langProviders)
        {
            if (p.Chapters != null)
                allChapters.AddRange(p.Chapters.Where(c => !string.IsNullOrEmpty(c.Filename)));
        }

        // Deduplicate by chapter number (prefer preferred provider)
        // Track the contributing provider for each chapter to detect multi-source feeds
        var dedupedChapters = new List<(Models.Chapter Chapter, SeriesProviderEntity Provider)>();
        foreach (var group in allChapters.GroupBy(c => c.ChapterNumber))
        {
            var winner = group.OrderByDescending(c =>
            {
                var prov = langProviders.FirstOrDefault(p => p.Chapters?.Any(pc => pc.ChapterNumber == c.ChapterNumber) == true);
                return prov?.IsUninstalled == false ? 1 : 0;
            }).First();

            var winnerProvider = langProviders.FirstOrDefault(p =>
                p.Chapters?.Any(pc => pc.ChapterNumber == winner.ChapterNumber && pc.Filename == winner.Filename) == true);

            dedupedChapters.Add((winner, winnerProvider ?? langProviders.First()));
        }
        dedupedChapters = dedupedChapters.OrderBy(dc => dc.Chapter.ChapterNumber).ToList();

        // Determine if multiple source providers contributed chapters (for [Source] suffix)
        var distinctProviders = dedupedChapters
            .Select(dc => dc.Provider.Provider)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToList();
        bool multiSource = distinctProviders.Count > 1;

        // Get read states
        var readStates = _readStateService.GetSeriesReadStates(user.Username, series.StoragePath ?? "");

        var sb = RenderHeader(user, $"series:{seriesId}:language:{language}", $"{series.Title} ({language})", user_agent);

        foreach (var (chapter, provider) in dedupedChapters)
        {
            string chapterTitle;
            if (dedupedChapters.Count > 1)
                chapterTitle = $"{series.Title} {chapter.ChapterNumber}";
            else
                chapterTitle = series.Title;
            sb.Append(await BuildChapterEntryAsync(user, seriesId, language, series, provider, chapter, readStates, chapterTitle, multiSource));
        }
        RenderFooter(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Builds the categories feed with entries from the configured categories.
    /// Each category entry gets a thumbnail from a random series in that category.
    /// </summary>
    public async Task<string> BuildCategoriesFeedAsync(UserEntity user, string user_agent, CancellationToken token = default)
    {
        var settings = _settingsService.DirectSettings;
        string[] categories = settings?.Categories ?? [];
        // Get all series to find thumbnails per category
        var allSeries = await _db.Series.AsNoTracking().ToListAsync(token);

        var sb = RenderHeader(user, "categories", "Categories", user_agent);

        foreach (var category in categories)
        {
            // Find series in this category (match against Genre list)
            var seriesInCategory = FilterByCategory(allSeries, category);
            if (seriesInCategory.Count>0)
                await RenderFolderAsync(sb, user, $"category:{EncodeBase64Url(category)}", category, seriesInCategory, null, token).ConfigureAwait(false);
        }
        RenderFooter(sb);
        return sb.ToString();
    }
    /// <summary>
    /// Builds the categories feed with entries from the configured tags.
    /// Each tags entry gets a thumbnail from a random series in that tag.
    /// </summary>
    public async Task<string> BuildTagsFeedAsync(UserEntity user, string user_agent, CancellationToken token = default)
    {
        // Get all series to find thumbnails per tag
        var allSeries = await _db.Series.AsNoTracking().ToListAsync(token);
        List<string> allTags = allSeries.SelectMany(a=>a.Genre).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a=>a).ToList();
        var sb = RenderHeader(user, "tags", "Tags", user_agent);

        foreach (var tag in allTags)
        {
            // Find series in this tag (match against Genre list)
            var seriesInTag = FilterByTag(allSeries, tag);
            await RenderFolderAsync(sb, user, $"tag:{EncodeBase64Url(tag)}", tag, seriesInTag, null, token).ConfigureAwait(false);
        }
        RenderFooter(sb);
        return sb.ToString();
    }



    /// <summary>
    /// Builds the sources feed with entries from the configured sources.
    /// Each source entry gets a thumbnail from a random series in that source.
    /// </summary>
    public async Task<string> BuildSourcesFeedAsync(UserEntity user, string user_agent, CancellationToken token = default)
    {
        // Get all series to find thumbnails per provider
        var allSeries = await _db.Series.Include(a=>a.Sources).AsNoTracking().ToListAsync(token);
        List<string> allSources = allSeries.SelectMany(a => a.Sources.Where(a=>!string.IsNullOrEmpty(a.Provider)).Select(b=>b.Provider)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a).ToList();
        List<ProviderStorageEntity> provs = await _db.Providers.GroupBy(a=>a.Name).Select(a=>a.First()).ToListAsync(token);
        Dictionary<string, ProviderStorageEntity> provDict = provs.ToDictionary(a => a.Name, a => a);
        var sb = RenderHeader(user, "sources", "Sources", user_agent);

        foreach (var source in allSources)
        {

            if (provDict.TryGetValue(source, out var prov) && !string.IsNullOrEmpty(prov?.ThumbnailUrl))
            {
                string thumb = await BuildThumbnailLinkAsync(user.OpdsPath, prov.ThumbnailUrl, token);
                RenderFolder(sb, user, $"source:{EncodeBase64Url(source)}", source, thumb, null);
            }
            else
            {
                // Find series in this source (match against Source list)
                var seriesInSource = FilterBySource(allSeries, source);
                await RenderFolderAsync(sb, user, $"source:{EncodeBase64Url(source)}", source, seriesInSource, null, token).ConfigureAwait(false);
            }
        }

        RenderFooter(sb);
        return sb.ToString();
    }
    private async Task<string> BuildLanguageSelectionFeedAsync(UserEntity user, string user_agent, SeriesEntity series, List<string> languages, CancellationToken token = default)
    {
        var sb = RenderHeader(user, $"series:{series.Id}:languages", $"Select Language - {series.Title}", user_agent);
        foreach (var lang in languages.OrderBy(l => l))
        {
            string thumbnailLinks = await BuildThumbnailLinkAsync(user.OpdsPath, series.ThumbnailUrl, token).ConfigureAwait(false);
            RenderFolder(sb, user, $"series:{series.Id}:language:{lang}", $"{series.Title} [{lang}]", thumbnailLinks, series.LastChapterDate);
        }
        RenderFooter(sb);
        return sb.ToString();
    }

    private static string BuildErrorFeed(string message)
    {
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <id>rensaio:error</id>
  <title>Error - {EscapeXml(message)}</title>
  <updated>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</updated>
</feed>";
    }

    /// <summary>
    /// Determines the image content type for an archive filename.
    /// </summary>
    private static string GetChapterContentType(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "";
        string ext = System.IO.Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".cbz" => "application/x-cbz",
            ".cbr" => "application/x-cbr",
            ".cb7" => "application/x-cb7",
            ".cbt" => "application/x-cbt",
            ".rar" => "application/vnd.rar",
            ".zip" => "application/zip",
            ".tar.gz" => "application/gzip",
            ".gz" => "application/gzip",
            ".7z" => "application/x-7z-compressed",
            ".7zip" => "application/x-7z-compressed",
            _ => "application/octet-stream"
        };
    }
    public static string EncodeBase64Url(string? str)
    {
        try
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(str ?? "")).TrimEnd('=').Replace('+', '-').Replace('/', '_') ?? "";
        }
        catch
        {
            return "";
        }
    }

    private List<SeriesEntity> FilterByTag(List<SeriesEntity> seriesList, string tag)
    {
        return seriesList.Where(a => a.Genre.Any(g => g.Equals(tag, StringComparison.OrdinalIgnoreCase))).ToList();
    }
    private List<SeriesEntity> FilterByCategory(List<SeriesEntity> seriesList, string category)
    {
        string b1 = category + "/";
        string b2 = category + "\\";
        return seriesList.Where(a => a.StoragePath.StartsWith(b1, true, System.Globalization.CultureInfo.InvariantCulture) || a.StoragePath.StartsWith(b2, true, System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }
    private string FindCategory(SeriesEntity series)
    {
        var settings = _settingsService.DirectSettings;
        string[] categories = settings?.Categories ?? [];
        string path = series.StoragePath.Replace('\\', '/');
        foreach (var category in categories)
        {
            string b1 = category + "/";
            if (path.StartsWith(b1, true, System.Globalization.CultureInfo.InvariantCulture))
                return category;
        }
        return "";
    }
    private List<SeriesEntity> FilterBySource(List<SeriesEntity> seriesList, string provider)
    {
        return seriesList.Where(a => a.Sources.Any(b => string.Equals(provider, b.Provider, StringComparison.InvariantCultureIgnoreCase))).ToList();
    }
    private DateTime LastDateTime(DateTime one, DateTime two)
    {
        if (one > two)
            return one;
        return two;
    }

    private List<SeriesEntity> OrderByLastReadOrLastChapter(List<(SeriesEntity Series, List<ChapterReadState> ChaptersReadState)> seriesList)
    {
        return seriesList.OrderByDescending(a => LastDateTime(a.ChaptersReadState.Max(b => b.LastReadAt), a.Series.LastChapterDate ?? DateTime.MinValue)).Select(a => a.Series).ToList();
    }

    private List<SeriesEntity> FilterByReading(List<SeriesEntity> seriesList, string username)
    {
        var res = _readStateService.GetUserSeriesReadStates(username, seriesList);
        return OrderByLastReadOrLastChapter(res);
    }
    private List<SeriesEntity> FilterByLast(List<SeriesEntity> seriesList)
    {
        DateTime last = DateTime.UtcNow.AddMonths(-2); // TODO: Move to appSettings
        return seriesList.Where(a => a.Sources.SelectMany(s => s.Chapters).Select(b => b.DownloadDate ?? DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max() > last).ToList();
    }

  

    /// <summary>
    /// Picks a random thumbnail from a list of series and builds the link elements.
    /// Falls back to empty string if no series with thumbnails are available.
    /// </summary>
    private async Task<string> PickRandomThumbnailAsync(string opdsPath, List<SeriesEntity> series, CancellationToken token)
    {
        var seriesWithThumb = series.Where(s => !string.IsNullOrWhiteSpace(s.ThumbnailUrl)).ToList();

        if (seriesWithThumb.Count == 0)
            return string.Empty;

        var random = new Random();
        var pick = seriesWithThumb[random.Next(seriesWithThumb.Count)];
        return await BuildThumbnailLinkAsync(opdsPath, pick.ThumbnailUrl, token);
    }
    private async Task<string> PickFirstThumbnailAsync(string opdsPath, List<SeriesEntity> series, CancellationToken token)
    {
        var seriesWithThumb = series.Where(s => !string.IsNullOrWhiteSpace(s.ThumbnailUrl)).ToList();

        if (seriesWithThumb.Count == 0)
            return string.Empty;

        var pick = seriesWithThumb[0];
        return await BuildThumbnailLinkAsync(opdsPath, pick.ThumbnailUrl, token);
    }
    /// <summary>
    /// Builds the OPDS thumbnail link elements for a series entry.
    /// Resolves the thumbnail URL to a cache key and links directly to /api/image/{key}.
    /// Emits both image and image/thumbnail link relations per OPDS 1.2 spec.
    /// Returns an empty string if the series has no thumbnail.
    /// </summary>
    private async Task<string> BuildThumbnailLinkAsync(string opdsPath, string? thumbnailUrl, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
            return string.Empty;

        string key = await _thumbCache.GetKeyAsync(thumbnailUrl, token);
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        // Use the OPDS-authenticated path instead of the raw /api/image/{key} endpoint
        string href = EscapeXml($"/{opdsPath}/image/{key}");
        return $@"  <link rel=""http://opds-spec.org/image"" href=""{href}""/>
    <link rel=""http://opds-spec.org/image/thumbnail"" href=""{href}""/>";
    }

    public static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
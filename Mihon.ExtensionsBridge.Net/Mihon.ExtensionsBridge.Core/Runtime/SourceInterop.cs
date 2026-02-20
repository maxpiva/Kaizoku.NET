using android;
using androidx.preference;
using eu.kanade.tachiyomi.network;
using eu.kanade.tachiyomi.source;
using eu.kanade.tachiyomi.source.model;
using Microsoft.Extensions.Logging;
using okhttp3;
using System.Reflection;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using Page = Mihon.ExtensionsBridge.Models.Extensions.Page;
using System.Net;

namespace Mihon.ExtensionsBridge.Core.Runtime
{
    public class SourceInterop : ISourceInterop
    {
        /// <summary>
        /// Logger for diagnostic and error reporting.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Underlying Tachiyomi source instance.
        /// </summary>
        eu.kanade.tachiyomi.source.Source _source;

        /// <summary>
        /// Cast view of the source as <see cref="eu.kanade.tachiyomi.source.online.HttpSource"/> when supported; otherwise <c>null</c>.
        /// </summary>
        eu.kanade.tachiyomi.source.online.HttpSource? _httpSource => _source as eu.kanade.tachiyomi.source.online.HttpSource;

        /// <summary>
        /// Cast view of the source as <see cref="eu.kanade.tachiyomi.source.online.ParsedHttpSource"/> when supported; otherwise <c>null</c>.
        /// </summary>
        eu.kanade.tachiyomi.source.online.ParsedHttpSource? _parsedHttpSource => _source as eu.kanade.tachiyomi.source.online.ParsedHttpSource;

        /// <summary>
        /// Cast view of the source as <see cref="eu.kanade.tachiyomi.source.CatalogueSource"/> when supported; otherwise <c>null</c>.
        /// </summary>
        eu.kanade.tachiyomi.source.CatalogueSource? _catalogueSource => _source as eu.kanade.tachiyomi.source.CatalogueSource;

        /// <summary>
        /// Cast view of the source as <see cref="eu.kanade.tachiyomi.source.ConfigurableSource"/> when supported; otherwise <c>null</c>.
        /// </summary>
        eu.kanade.tachiyomi.source.ConfigurableSource? _configurableSource => _source as eu.kanade.tachiyomi.source.ConfigurableSource;

        /// <summary>
        /// Cached preference screen built from the configurable source, if available.
        /// </summary>
        PreferenceScreen _preference = null;

        /// <summary>
        /// Cached filter list for catalogue queries. Populated on first access.
        /// </summary>
        public eu.kanade.tachiyomi.source.model.FilterList _cachedList = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="SourceInterop"/> class.
        /// </summary>
        /// <param name="source">The Tachiyomi source to wrap.</param>
        /// <param name="logger">The logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="logger"/> is <c>null</c>.</exception>
        public SourceInterop(eu.kanade.tachiyomi.source.Source source, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Gets the unique identifier of the source.
        /// </summary>
        public long Id => _source.getId();

        /// <summary>
        /// Gets the display name of the source.
        /// </summary>
        public string Name => _source.getName();

        /// <summary>
        /// Gets the language code of the source.
        /// </summary>
        public string Language => _source.getLang();


        public string BaseUrl => _httpSource?.getBaseUrl() ?? string.Empty;

        public int VersionId => _httpSource?.getVersionId() ?? 0;
        
        /// <summary>
        /// Gets a value indicating whether the source supports HTTP operations.
        /// </summary>
        public bool IsHttpSource => _httpSource != null;

        /// <summary>
        /// Gets a value indicating whether the source is a parsed HTTP source.
        /// </summary>
        public bool IsParsedHttpSource => _parsedHttpSource != null;

        /// <summary>
        /// Gets a value indicating whether the source supports catalogue operations.
        /// </summary>
        public bool IsCatalogueSource => _catalogueSource != null;

        /// <summary>
        /// Gets a value indicating whether the source exposes a configurable preference screen.
        /// </summary>
        public bool IsConfigurableSource => _configurableSource != null;

        /// <summary>
        /// Gets a value indicating whether the source supports latest updates listing.
        /// </summary>
        public bool SupportsLatest => _catalogueSource != null && _catalogueSource.getSupportsLatest();

        /// <summary>
        /// Ensures the catalogue filter list is populated and cached.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the source does not support catalogue operations.</exception>
        private void PopulateFilterList()
        {
            if (_catalogueSource == null)
                throw new InvalidOperationException("Source does not support catalogue operations.");

            if (_cachedList == null)
            {
                _cachedList = _catalogueSource.getFilterList();
            }
        }
        public async Task<T> WrapHttpException<T>(Func<Task<T>> func)
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch(java.io.IOException ioe)
            {
                throw new HttpRequestException(ioe.getMessage(), ioe);
            }
            catch(HttpException ex)
            {
                throw new HttpRequestException(ex.getMessage(), ex, (HttpStatusCode)ex.getCode());
            }
            catch(Exception z)
            {
                throw;
            }
        }


        private static MangasPage EmptyMangasPage() => new MangasPage(new java.util.ArrayList(), false);

        // Generic helper to create default instance via factory

        public async Task<MangaList> GetPopularAsync(int page, CancellationToken token = default)
        {
            return await WrapHttpException(async () => 
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support catalogue operations.");
                var mangaPage = await _httpSource.fetchPopularManga(page).ConsumeObservableOneOrDefaultAsync<MangasPage>(EmptyMangasPage(), token).ConfigureAwait(false);
                return mangaPage!.ToMangaList(_httpSource);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves the latest updates for the specified page.
        /// </summary>
        /// <param name="page">The page number.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>A <see cref="MangaList"/> containing latest items.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the source does not support catalogue operations or latest updates.
        /// </exception>
        public async Task<MangaList> GetLatestAsync(int page, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support catalogue operations.");
                if (!SupportsLatest)
                    throw new InvalidOperationException("Source does not support latest updates.");
                var mangaPage = await _httpSource.fetchLatestUpdates(page).ConsumeObservableOneOrDefaultAsync<MangasPage>(EmptyMangasPage(), token).ConfigureAwait(false);
                return mangaPage!.ToMangaList(_httpSource);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Searches the catalogue with the given query and cached filters.
        /// </summary>
        /// <param name="page">The page number.</param>
        /// <param name="query">The search query text.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>A <see cref="MangaList"/> matching the query.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the source does not support catalogue operations.</exception>
        public async Task<MangaList> SearchAsync(int page, string query, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support catalogue operations.");
                PopulateFilterList();
                var mangaPage = await _httpSource.fetchSearchManga(page, query, _cachedList).ConsumeObservableOneOrDefaultAsync<MangasPage>(EmptyMangasPage(), token).ConfigureAwait(false);
                return mangaPage!.ToMangaList(_httpSource);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetches detailed information for a manga.
        /// </summary>
        /// <param name="manga">The manga model to enrich.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The detailed <see cref="Manga"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the source does not support HTTP operations.</exception>
        public async Task<ParsedManga> GetDetailsAsync(Manga manga, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support catalogue operations.");
                SManga mangaImpl = manga.ToSManga();
                var mangaDetails = await _httpSource.fetchMangaDetails(mangaImpl).ConsumeObservableOneOrDefaultAsync<SManga>(mangaImpl, token).ConfigureAwait(false);
                ParsedManga m = mangaDetails!.ToManga<ParsedManga>(manga);
                m.RealUrl = _httpSource.getMangaUrl(mangaDetails ?? mangaImpl);
                return m;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves the chapter list for a given manga.
        /// </summary>
        /// <param name="manga">The target manga.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>A list of <see cref="Chapter"/> items.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the source does not support HTTP operations.</exception>
        public async Task<List<ParsedChapter>> GetChaptersAsync(Manga manga, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support catalogue operations.");
                SManga mangaImpl = manga.ToSManga();
                var chapters = await _httpSource.fetchChapterList(mangaImpl).ConsumeObservableOneOrDefaultAsync<java.util.List>(new java.util.ArrayList(), token).ConfigureAwait(false);
                return chapters!.toArray().Cast<SChapter>().ToParsedChapters(manga.Title, mangaImpl, _httpSource);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves page list for the specified chapter.
        /// </summary>
        /// <param name="chapter">The target chapter.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>A list of <see cref="Page"/> items.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the source does not support HTTP operations.</exception>
        public async Task<List<Page>> GetPagesAsync(Chapter chapter, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support catalogue operations.");
                SChapterImpl chasImpl = chapter.ToSChapter();
                var pages = await _httpSource.fetchPageList(chasImpl).ConsumeObservableOneOrDefaultAsync<java.util.List>(new java.util.ArrayList(), token).ConfigureAwait(false);
                return pages!.toArray().Cast<eu.kanade.tachiyomi.source.model.Page>().Select(a => a.ToPage()).ToList();
            }).ConfigureAwait(false);
        }
        public async Task<ContentTypeStream> DownloadUrlAsync(string url, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support http operations.");
                if (string.IsNullOrEmpty(url))
                    throw new ArgumentNullException(nameof(url));
                Request r = RequestsKt.GET(url, _httpSource.getHeaders(), CacheControl.FORCE_NETWORK);
                // Replace the direct call with an awaited execution to get the Response asynchronously
                var response = await Task.Run(() => _httpSource.getClient().newCall(r).execute(), token).ConfigureAwait(false);
                if (response == null)
                    throw new HttpRequestException("Image response was null.");
                if (response.code() != 200)
                    throw new HttpRequestException($"Request error! {response.code()}",null, (HttpStatusCode)response.code());
                return new ContentTypeStreamImplementation(response);
            }).ConfigureAwait(false);
        }
        public async Task<ContentTypeStream> GetPageImageAsync(Page page, CancellationToken token = default)
        {
            return await WrapHttpException(async () =>
            {
                if (_httpSource == null)
                    throw new InvalidOperationException("Source does not support http operations.");
                if (string.IsNullOrEmpty(page.ImageUrl))
                {
                    var spage2 = page.ToSPage();
                    string newImageUrl = await _httpSource.fetchImageUrl(spage2).ConsumeObservableOneOrDefaultAsync<string>(string.Empty, token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(newImageUrl))
                        page.ImageUrl = newImageUrl;
                }

                if (string.IsNullOrEmpty(page.ImageUrl))
                    throw new ArgumentException("Page URL is null or empty.", nameof(page.ImageUrl));

                var spage = page.ToSPage();
                Response? response = await KotlinSuspendBridge.CallSuspend<Response>((cont) =>
                {
                    return _httpSource.getImage(spage, cont);
                }, token).ConfigureAwait(false);
                if (response == null)
                    throw new HttpRequestException("Image response was null.");
                if (response.code() != 200)
                    throw new HttpRequestException($"Request error! {response.code()}", null, (HttpStatusCode)response.code());
                return new ContentTypeStreamImplementation(response);
            }).ConfigureAwait(false);
        }



        /// <summary>
        /// Builds and returns a list of preferences exposed by the configurable source.
        /// </summary>
        /// <returns>A list of bridge <see cref="Models.Extensions.Preference"/> items.</returns>
        /// <remarks>
        /// The preference screen is cached on first invocation. Subsequent calls reuse the cached screen.
        /// </remarks>
        public List<KeyPreference> GetPreferences()
        {
            if (_configurableSource == null)
                return new List<KeyPreference>();
            List<androidx.preference.Preference> prefs;

            if (_preference == null)
            {
                android.content.SharedPreferences sourceSharedPreferences;                                  
                sourceSharedPreferences = ConfigurableSourceKt.sourcePreferences(_configurableSource);
                _preference = new ConfigurableSource.PrefsHelper().createScreen();
                _preference.setSharedPreferences(sourceSharedPreferences);
                _configurableSource.setupPreferenceScreen(_preference);
            }
            prefs = GetPreferencesFromScreen(_preference);
            return prefs.Select((p, index) => p.ToKeyPreference(index)).ToList();
        }

        /// <summary>
        /// Extracts all preferences from a <see cref="PreferenceScreen"/>.
        /// </summary>
        /// <param name="screen">The source preference screen.</param>
        /// <returns>A list of AndroidX preference objects contained in the screen.</returns>
        private static List<androidx.preference.Preference> GetPreferencesFromScreen(PreferenceScreen screen)
        {
            var list = new List<androidx.preference.Preference>();
            foreach (androidx.preference.Preference p in screen.getPreferences().toArray().Cast<androidx.preference.Preference>())
            {
                list.Add(p);
            }
            return list;
        }



        private object GetValueFromPreference(androidx.preference.Preference p, string value)
        {
            var type = p.getDefaultValueType(); // assuming your IKVM port has getDefaultValueType()
            if (type == "String")
                return value;
            else if (type == "Boolean")
                return new java.lang.Boolean(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)); // matches Kotlin toBoolean()
            else if (type == "Set<String>")
            {
                string[]? vals = System.Text.Json.JsonSerializer.Deserialize<string[]>(value);
                if (vals == null)
                    return new java.util.ArrayList();
                else
                {
                    var result = new java.util.ArrayList(vals.Length);
                    foreach (var v in vals)
                        result.add(v);
                    return result;
                }
            }
            throw new InvalidOperationException("Unsupported type conversion");
        }
        public void SetPreference(KeyPreference preference)
        {
            if (preference == null)
                throw new ArgumentNullException(nameof(preference));
            if (_preference == null)
                throw new InvalidOperationException("No PreferenceScreen cached. Call GetSourcePreferences first.");
            if (!_preference.isEnabled())
                return;
            SetPreference(preference.Index, preference.CurrentValue);
        }
        public void SetPreferences(IEnumerable<KeyPreference> preferences)
        {
            if (preferences == null)
                throw new ArgumentNullException(nameof(preferences));
            if (_preference == null)
                throw new InvalidOperationException("No PreferenceScreen cached. Call GetSourcePreferences first.");
            if (!_preference.isEnabled())
                return;
            foreach (ExtensionsBridge.Models.Extensions.Preference p in preferences)
                SetPreference(p.Index, p.CurrentValue);
        }
        public void SetPreference(int position, string value)
        {
            if (_preference == null)
                throw new InvalidOperationException("No PreferenceScreen cached. Call GetSourcePreferences first.");
            if (!_preference.isEnabled())
                return;
            androidx.preference.Preference pref = (androidx.preference.Preference)_preference.getPreferences().get(position);
            var newValue = GetValueFromPreference(pref, value);
            pref.saveNewValue(newValue);
            pref.callChangeListener(newValue);
        }
    }
}

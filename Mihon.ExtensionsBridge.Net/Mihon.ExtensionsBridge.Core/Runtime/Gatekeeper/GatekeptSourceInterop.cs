using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Core.Runtime.Gatekeeper
{
    // Wrapper for ISourceInterop that coordinates entry/exit with gatekeeper
    internal sealed class GatekeptSourceInterop : ISourceInterop
    {
        private readonly GatekeptExtensionInterop _gate;
        private readonly ISourceInterop _inner;
        private readonly ILogger _logger;

        public GatekeptSourceInterop(GatekeptExtensionInterop gate, ISourceInterop inner, ILogger logger)
        {
            _gate = gate;
            _inner = inner;
            _logger = logger;
        }

        public long Id => _inner.Id;
        public bool IsCatalogueSource => _inner.IsCatalogueSource;
        public bool IsConfigurableSource => _inner.IsConfigurableSource;
        public bool IsHttpSource => _inner.IsHttpSource;
        public bool IsParsedHttpSource => _inner.IsParsedHttpSource;
        public string Language => _inner.Language;
        public string Name => _inner.Name;
        public bool SupportsLatest => _inner.SupportsLatest;

        public async Task<List<ParsedChapter>> GetChaptersAsync(Manga manga, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.GetChaptersAsync(manga, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<ParsedManga> GetDetailsAsync(Manga manga, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.GetDetailsAsync(manga, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<ContentTypeStream> DownloadUrlAsync(string url, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.DownloadUrlAsync(url, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<ContentTypeStream> GetPageImageAsync(Page page, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.GetPageImageAsync(page, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<MangaList> GetLatestAsync(int page, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.GetLatestAsync(page, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<List<Page>> GetPagesAsync(Chapter chapter, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.GetPagesAsync(chapter, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<MangaList> GetPopularAsync(int page, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.GetPopularAsync(page, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public async Task<MangaList> SearchAsync(int page, string query, CancellationToken token = default)
        { await _gate.EnterAsync(token); try { return await _inner.SearchAsync(page, query, token).ConfigureAwait(false); } finally { _gate.Exit(); } }
        public List<KeyPreference> GetPreferences()
        { return _inner.GetPreferences(); }
        public void SetPreference(int position, string value)
        { _inner.SetPreference(position, value); }
        public void SetPreference(KeyPreference preference)
        { _inner.SetPreference(preference); }
        public void SetPreferences(IEnumerable<KeyPreference> preferences)
        { _inner.SetPreferences(preferences); }
    }
}

using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface ISourceInterop
    {
        long Id { get; }
        bool IsCatalogueSource { get; }
        bool IsConfigurableSource { get; }
        bool IsHttpSource { get; }
        bool IsParsedHttpSource { get; }
        string Language { get; }
        string Name { get; }
        bool SupportsLatest { get; }

        Task<List<Chapter>> GetChaptersAsync(Manga manga, CancellationToken token = default);
        Task<Manga> GetDetailsAsync(Manga manga, CancellationToken token = default);
        Task<ContentTypeStream> GetPageImageAsync(Page page, CancellationToken token = default);
        Task<ContentTypeStream> DownloadUrlAsync(string url, CancellationToken token = default);
        Task<MangaList> GetLatestAsync(int page, CancellationToken token = default);
        Task<List<Page>> GetPagesAsync(Chapter chapter, CancellationToken token = default);
        Task<MangaList> GetPopularAsync(int page, CancellationToken token = default);
        Task<MangaList> SearchAsync(int page, string query, CancellationToken token = default);
        List<KeyPreference> GetPreferences();
        void SetPreference(int position, string value);
        void SetPreference(KeyPreference preference);
        void SetPreferences(IEnumerable<KeyPreference> preferences);
    }

}

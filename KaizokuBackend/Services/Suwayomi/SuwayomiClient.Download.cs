using System.Net.Http.Json;
using KaizokuBackend.Models;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        /// <summary>
        /// Queues a chapter for download
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <returns>True if successfully queued</returns>
        public async Task<bool> QueueChapterDownloadAsync(int mangaId, int chapterIndex, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/download/{mangaId}/chapter/{chapterIndex}";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Removes a chapter from download queue
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <returns>True if successfully removed</returns>
        public async Task<bool> UnqueueChapterDownloadAsync(int mangaId, int chapterIndex, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/download/{mangaId}/chapter/{chapterIndex}";
            var response = await _http.DeleteAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Reorders a chapter in download queue
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <param name="toPosition">New position in queue</param>
        /// <returns>True if successfully reordered</returns>
        public async Task<bool> ReorderChapterDownloadAsync(int mangaId, int chapterIndex, int toPosition, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/download/{mangaId}/chapter/{chapterIndex}/reorder/{toPosition}";
            var response = await _http.PatchAsync(url, null, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Queues multiple chapters for download
        /// </summary>
        /// <param name="chapters">List of chapter identifiers (mangaId, chapterIndex pairs)</param>
        /// <returns>True if successfully queued</returns>
        public async Task<bool> QueueMultipleChaptersDownloadAsync(List<(int MangaId, int ChapterIndex)> chapters, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/download/batch";
            
            var payload = chapters.Select(c => new Dictionary<string, int>
            {
                { "mangaId", c.MangaId },
                { "chapterIndex", c.ChapterIndex }
            }).ToList();
            
            var response = await _http.PostAsJsonAsync(url, payload, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Removes multiple chapters from download queue
        /// </summary>
        /// <param name="chapters">List of chapter identifiers (mangaId, chapterIndex pairs)</param>
        /// <returns>True if successfully removed</returns>
        public async Task<bool> UnqueueMultipleChaptersDownloadAsync(List<(int MangaId, int ChapterIndex)> chapters, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/download/batch";
            
            var payload = chapters.Select(c => new Dictionary<string, int>
            {
                { "mangaId", c.MangaId },
                { "chapterIndex", c.ChapterIndex }
            }).ToList();
            
            var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = JsonContent.Create(payload)
            };
            
            var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
    }
}
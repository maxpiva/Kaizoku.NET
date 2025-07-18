using System.Net.Http.Json;
using KaizokuBackend.Models;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        
        /// <summary>
        /// Performs batch operations on chapters across different mangas
        /// </summary>
        /// <param name="operations">Dictionary containing operation type and chapter IDs</param>
        /// <returns>True if successful</returns>
        public async Task<bool> BatchAnyChapterOperationAsync(Dictionary<string, object> operations, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/chapter/batch";
            var response = await _http.PostAsJsonAsync(url, operations, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Downloads a chapter by its ID
        /// </summary>
        /// <param name="chapterId">ID of the chapter</param>
        /// <returns>Downloaded chapter file as byte array or null if failed</returns>
        public async Task<Stream> DownloadChapterAsync(int chapterId, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/chapter/{chapterId}/download";
                var response = await _http.GetAsync(url, token).ConfigureAwait(false);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException)
            {
                // Log error and return null
            }
            
            return new MemoryStream();
        }

        /// <summary>
        /// Checks if a chapter is available for download
        /// </summary>
        /// <param name="chapterId">ID of the chapter</param>
        /// <returns>True if available</returns>
        public async Task<bool> IsChapterDownloadAvailableAsync(int chapterId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/chapter/{chapterId}/download";
            var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
    }
}
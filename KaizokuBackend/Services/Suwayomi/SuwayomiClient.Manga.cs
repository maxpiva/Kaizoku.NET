using System.Net;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using System.Net.Http.Json;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        /// <summary>
        /// Gets a list of chapters for a manga
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="onlineFetch">Whether to fetch latest data from online source</param>
        /// <returns>List of chapters</returns>
        public async Task<List<SuwayomiChapter>?> GetChaptersAsync(int mangaId, bool onlineFetch = true, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/manga/{mangaId}/chapters?onlineFetch={onlineFetch.ToString().ToLower()}";
                return await (_http.GetFromJsonAsync<List<SuwayomiChapter>>(url, token).ConfigureAwait(false)) ?? new();

            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets information about a specific chapter
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <returns>Chapter object</returns>
        public async Task<SuwayomiChapter?> GetChapterAsync(int mangaId, int chapterIndex, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/manga/{mangaId}/chapter/{chapterIndex}";
                return await _http.GetFromJsonAsync<SuwayomiChapter>(url, token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>
        /// Updates chapter information
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <param name="chapterData">Chapter data to update</param>
        /// <returns>Updated Chapter object</returns>
        public async Task<SuwayomiChapter?> UpdateChapterAsync(int mangaId, int chapterIndex, Dictionary<string, object> chapterData, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/chapter/{chapterIndex}";
            var response = await _http.PatchAsJsonAsync(url, chapterData, token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SuwayomiChapter>(token).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Deletes a chapter
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <returns>True if successful</returns>
        public async Task<bool> DeleteChapterAsync(int mangaId, int chapterIndex, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/chapter/{chapterIndex}";
            var response = await _http.DeleteAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Updates chapter metadata
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <param name="metadata">Metadata dictionary</param>
        /// <returns>Updated Chapter object</returns>
        public async Task<SuwayomiChapter?> UpdateChapterMetadataAsync(int mangaId, int chapterIndex, Dictionary<string, object> metadata, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/chapter/{chapterIndex}/meta";
            var response = await _http.PatchAsJsonAsync(url, metadata, token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SuwayomiChapter>(token).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Gets a specific page from a chapter
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndex">Index of the chapter</param>
        /// <param name="pageIndex">Index of the page</param>
        /// <returns>Page image as byte array</returns>
        public async Task<(HttpStatusCode code, Stream? Stream)> GetPageAsync(int mangaId, int chapterIndex, int pageIndex, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/manga/{mangaId}/chapter/{chapterIndex}/page/{pageIndex}";
                var response = await _http.GetAsync(url, token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return (HttpStatusCode.OK, (await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false)));
                }
                return (response.StatusCode, null);
            }
            catch (HttpRequestException e)
            {
                return (e.StatusCode ?? HttpStatusCode.ServiceUnavailable, null);
            }

        }

        /// <summary>
        /// Performs batch operations on chapters
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="chapterIndexes">List of chapter indexes</param>
        /// <param name="operation">Operation to perform (e.g., "read", "unread")</param>
        /// <returns>True if successful</returns>
        public async Task<bool> BatchChapterOperationAsync(int mangaId, List<int> chapterIndexes, string operation, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/chapter/batch";

            var payload = new Dictionary<string, object>
            {
                { "chapterIndexes", chapterIndexes },
                { "operation", operation }
            };

            var response = await _http.PostAsJsonAsync(url, payload, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Gets basic information about a manga
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <returns>Series object with basic information</returns>
        public async Task<SuwayomiSeries?> GetMangaAsync(int mangaId, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/manga/{mangaId}";
                return await _http.GetFromJsonAsync<SuwayomiSeries>(url, token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Log error and return null explicitly
                return null;
            }
        }

        /// <summary>
        /// Gets full information about a manga including details that might need to be fetched from source
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="onlineFetch">Whether to fetch latest data from online source</param>
        /// <returns>Series object with full details</returns>
        public async Task<SuwayomiSeries?> GetFullSeriesDataAsync(int mangaId, bool onlineFetch = true, CancellationToken token = default)
        {
            try
            {

                var url = $"{_apiUrl}/manga/{mangaId}/full?onlineFetch={onlineFetch.ToString().ToLower()}";
                return await _http.GetFromJsonAsync<SuwayomiSeries>(url, token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Log error and return null explicitly
                return null;
            }
        }

        /// <summary>
        /// Gets the thumbnail image for a manga
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <returns>Thumbnail image as byte array</returns>
        public async Task<Stream> GetMangaThumbnailAsync(int mangaId, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/manga/{mangaId}/thumbnail";
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
        /// Gets categories that a manga belongs to
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <returns>List of category IDs</returns>
        public async Task<List<int>> GetMangaCategoriesAsync(int mangaId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/category";
            return await (_http.GetFromJsonAsync<List<int>>(url, token).ConfigureAwait(false)) ?? new();
        }

        /// <summary>
        /// Adds a manga to a category
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="categoryId">ID of the category</param>
        /// <returns>True if successful</returns>
        public async Task<bool> AddMangaToCategoryAsync(int mangaId, int categoryId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/category/{categoryId}";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Removes a manga from a category
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="categoryId">ID of the category</param>
        /// <returns>True if successful</returns>
        public async Task<bool> RemoveMangaFromCategoryAsync(int mangaId, int categoryId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/category/{categoryId}";
            var response = await _http.DeleteAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Adds a manga to the library
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <returns>True if successful</returns>
        public async Task<bool> AddSeriesToLibraryAsync(int mangaId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/library";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Removes a manga from the library
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <returns>True if successful</returns>
        public async Task<bool> RemoveSeriesFromLibraryAsync(int mangaId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/library";
            var response = await _http.DeleteAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Updates metadata for a manga
        /// </summary>
        /// <param name="mangaId">ID of the manga</param>
        /// <param name="metadata">Metadata dictionary</param>
        /// <returns>Updated Series object</returns>
        public async Task<SuwayomiSeries?> UpdateMangaMetadataAsync(int mangaId, Dictionary<string, object> metadata, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/manga/{mangaId}/meta";
            var response = await _http.PatchAsJsonAsync(url, metadata, token).ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SuwayomiSeries>(token).ConfigureAwait(false);
            }
            
            return null;
        }
    }
}

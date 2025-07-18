using System.Net;
using KaizokuBackend.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;


namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        /// <summary>
        /// Gets a list of all available sources
        /// </summary>
        /// <returns>List of sources</returns>
        public async Task<List<SuwayomiSource>> GetSourcesAsync(CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/list";
            return (await _http.GetFromJsonAsync<List<SuwayomiSource>>(url, token).ConfigureAwait(false)) ?? new();
        }

        /// <summary>
        /// Gets popular series from a specific source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <param name="pageNum">Page number (defaults to 1)</param>
        /// <returns>List of popular series</returns>
        public Task<SuwayomiSeriesResult?> GetPopularSeriesAsync(string sourceId, int pageNum = 1, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/{sourceId}/popular/{pageNum}";
            return _http.GetFromJsonAsync<SuwayomiSeriesResult>(url, token);
        }

        /// <summary>
        /// Searches for series in a specific source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <param name="keywords">Search keyword</param>
        /// <param name="pageNum">Page number (defaults to 1)</param>
        /// <returns>List of matching series</returns>
        public Task<SuwayomiSeriesResult?> SearchSeriesAsync(string sourceId, string keywords, int pageNum = 1, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/{sourceId}/search?searchTerm={WebUtility.UrlEncode(keywords)}&pageNum={pageNum}";
            return _http.GetFromJsonAsync<SuwayomiSeriesResult>(url,token);
        }
        public Task<SuwayomiSeriesResult?> QuickSearchSeriesAsync(string sourceId, string keywords, int pageNum = 1, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/{sourceId}/quick-search?searchTerm={WebUtility.UrlEncode(keywords)}&pageNum={pageNum}";
            return _http.GetFromJsonAsync<SuwayomiSeriesResult>(url, token);
        }
        /// <summary>
        /// Gets latest series from a specific source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <param name="pageNum">Page number (defaults to 1)</param>
        /// <returns>List of latest series</returns>
        public Task<SuwayomiSeriesResult?> GetLatestSeriesAsync(string sourceId, int pageNum = 1, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/{sourceId}/latest/{pageNum}";
            return _http.GetFromJsonAsync<SuwayomiSeriesResult>(url, token);
        }

        /// <summary>
        /// Gets filters available for a specific source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <returns>List of filters as dictionary objects</returns>
        public async Task<List<Dictionary<string, object>>> GetSourceFiltersAsync(string sourceId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/{sourceId}/filters";
            return (await _http.GetFromJsonAsync<List<Dictionary<string, object>>>(url, token).ConfigureAwait(false)) ?? new();
        }

        /// <summary>
        /// Gets the icon for a source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <returns>Icon as byte array</returns>
        public async Task<Stream> GetSourceIconAsync(string sourceId, CancellationToken token = default)
        {
            try
            {
                var url = $"{_apiUrl}/source/{sourceId}/icon";
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
        /// Gets the preferences for a specific source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <returns>Source preferences as a dictionary object</returns>
        public async Task<List<SuwayomiPreference>> GetSourcePreferencesAsync(string sourceId, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/source/{sourceId}/preferences";
            return (await _http.GetFromJsonAsync< List<SuwayomiPreference>>(url, token).ConfigureAwait(false)) ?? new List<SuwayomiPreference>();
        }

        public class SourcePreferenceChange
        {
            [JsonPropertyName("position")]
            public int Position { get; set; }
            [JsonPropertyName("value")]
            public string Value { get; set; } = "";
        }

        /// <summary>
        /// Sets a preference for a specific source
        /// </summary>
        /// <param name="sourceId">ID of the source</param>
        /// <param name="preferenceKey">Key of the preference</param>
        /// <param name="preferenceValue">Value to set</param>
        /// <returns>True if successful</returns>
        public async Task<bool> SetSourcePreferenceAsync(string sourceId, int preferenceIndex, object preferenceValue, CancellationToken token = default)
        {
            int repeat = 0;
            bool ok = false;
            do
            {
                var url = $"{_apiUrl}/source/{sourceId}/preferences";
                object? obj = null;
                switch (preferenceValue.GetType().Name.ToLowerInvariant())
                {
                    case "string":
                        obj = new SourcePreferenceChange { Position = preferenceIndex, Value = (string)preferenceValue };
                        break;
                    case "boolean":
                        obj = new SourcePreferenceChange { Position = preferenceIndex, Value = ((bool)preferenceValue).ToString() };
                        break;
                    case "string[]":
                        obj = new SourcePreferenceChange { Position = preferenceIndex, Value = System.Text.Json.JsonSerializer.Serialize(preferenceValue) };
                        break;
                }
                var response = await _http.PostAsJsonAsync(url, obj, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await GetSourcePreferencesAsync(sourceId, token).ConfigureAwait(false);
                }
                repeat++;
                if (repeat == 3)
                    break;
                ok = response.IsSuccessStatusCode;
            } while (!ok);

            return true;
        }

        /// <summary>
        /// Helper class for manga list results
        /// </summary>

    }
}

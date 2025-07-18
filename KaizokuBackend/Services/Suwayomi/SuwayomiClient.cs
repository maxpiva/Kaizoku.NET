using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using System.Net.Http.Json;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        private readonly string _apiUrl;

        public SuwayomiClient(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
            bool capi = _config.GetValue<bool>("Suwayomi:UseCustomApi", false);
            if (capi)
            {
                _apiUrl = _config.GetValue<string>("Suwayomi:CustomEndpoint","");
                _apiUrl=_apiUrl.TrimEnd('/');
            }
            else
            {
                _apiUrl = "http://127.0.0.1:4567/api/v1";
            }
        }

        public async Task<SuwayomiSeriesResult?> GetLibraryAsync()
        {

            return await Task.FromResult(new SuwayomiSeriesResult() { MangaList = [] }).ConfigureAwait(false);
        }

    }
}

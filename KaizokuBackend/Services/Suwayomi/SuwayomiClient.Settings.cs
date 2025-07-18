
using KaizokuBackend.Models;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        private string LatestFragment = """
            fragment MANGA_BASE_FIELDS on MangaType {
              id
              title
              thumbnailUrl
              thumbnailUrlLastFetched
              inLibrary
              initialized
              sourceId
              __typename
            }

            mutation GET_SOURCE_MANGAS_FETCH($input: FetchSourceMangaInput!) {
              fetchSourceManga(input: $input) {
                hasNextPage
                mangas {
                  ...MANGA_BASE_FIELDS
                  __typename
                }
                __typename
              }
            }
            
            """;

        private string SettingsFragment = """
          fragment SERVER_SETTINGS on SettingsType {
          ip
          port
          socksProxyEnabled
          socksProxyVersion
          socksProxyHost
          socksProxyPort
          socksProxyUsername
          socksProxyPassword
          webUIFlavor
          initialOpenInBrowserEnabled
          webUIInterface
          electronPath
          webUIChannel
          webUIUpdateCheckInterval
          downloadAsCbz
          downloadsPath
          autoDownloadNewChapters
          excludeEntryWithUnreadChapters
          autoDownloadNewChaptersLimit
          autoDownloadIgnoreReUploads
          extensionRepos
          maxSourcesInParallel
          excludeUnreadChapters
          excludeNotStarted
          excludeCompleted
          globalUpdateInterval
          updateMangas
          basicAuthEnabled
          basicAuthUsername
          basicAuthPassword
          debugLogsEnabled
          systemTrayEnabled
          maxLogFileSize
          maxLogFiles
          maxLogFolderSize
          backupPath
          backupTime
          backupInterval
          backupTTL
          localSourcePath
          flareSolverrEnabled
          flareSolverrUrl
          flareSolverrTimeout
          flareSolverrSessionName
          flareSolverrSessionTtl
          flareSolverrAsResponseFallback
          __typename
          }
          
          query GET_SERVER_SETTINGS {
          settings {
            ...SERVER_SETTINGS
            __typename
          }
          }
          """;

        public class GraphQLQuery
        {
            public string OperationName { get; set; } ="";
            public Dictionary<string, Dictionary<string, object>> Variables { get; set; } = new();
            public string Query { get; set; } = "";
        }

        public async Task<Dictionary<string, object>> SetServerSettingsAsync(Dictionary<string, object> settings, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/graphql".Replace("/v1", "");
            GraphQLQuery query = new GraphQLQuery
            {
                OperationName = "UPDATE_SERVER_SETTINGS",
                Query = SettingsFragment

            };
            query.Variables.Add("input", new Dictionary<string, object>
            {
                { "settings", settings }
            });
            HttpResponseMessage msg = await _http.PostAsJsonAsync<GraphQLQuery>(url, query, token).ConfigureAwait(false);
            if (msg.IsSuccessStatusCode)
            {
                SettingsRoot? root = await msg.Content.ReadFromJsonAsync<SettingsRoot>(token).ConfigureAwait(false);
                if (root != null && root.Data != null)
                {
                    return root.Data.Settings;
                }
            }
            return new Dictionary<string, object>();
        }
        public async Task<Dictionary<string, object>> GetServerSettingsAsync(CancellationToken token = default)
        {
            var url = $"{_apiUrl}/graphql".Replace("/v1","");
            GraphQLQuery query = new GraphQLQuery
            {
                OperationName = "GET_SERVER_SETTINGS",
                Query = SettingsFragment
            };
            HttpResponseMessage msg = await _http.PostAsJsonAsync<GraphQLQuery>(url, query,token).ConfigureAwait(false);
            if (msg.IsSuccessStatusCode)
            {
                SettingsRoot? root = await msg.Content.ReadFromJsonAsync<SettingsRoot>(token).ConfigureAwait(false);
                if (root != null && root.Data != null)
                {
                    return root.Data.Settings;
                }
            }
            return new Dictionary<string, object>();
        }
        public async Task<SuwayomiGraphQLSeriesResult?> GetLatestAsync(string sourceid, int page = 1, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/graphql".Replace("/v1", "");
            GraphQLQuery query = new GraphQLQuery
            {
                OperationName = "GET_SOURCE_MANGAS_FETCH",
                Query = LatestFragment
            };
            query.Variables.Add("input", new Dictionary<string, object>
            {
                { "type", "LATEST" },
                { "source", sourceid },
                { "page", page}
            });

            HttpResponseMessage msg = await _http.PostAsJsonAsync<GraphQLQuery>(url, query, token).ConfigureAwait(false);
            if (msg.IsSuccessStatusCode)
            {
                LatestRoot? root = await msg.Content.ReadFromJsonAsync<LatestRoot>(token).ConfigureAwait(false);
                if (root != null && root.Data != null)
                {
                    return root.Data.FetchSourceManga;
                }
            }
            return null;
        }

        internal class SettingsData
        {
            public Dictionary<string, object> Settings { get; set; } = new();
        }

        internal class SettingsRoot
        {
            public SettingsData Data { get; set; } = new();
        }

        internal class LatestData
        {
            public SuwayomiGraphQLSeriesResult FetchSourceManga { get; set; } = new();
        }

        internal class LatestRoot
        {
            public LatestData Data { get; set; } = new();

        }
    }
}

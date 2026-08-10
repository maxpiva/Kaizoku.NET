using RensaioBackend.Extensions;
using RensaioBackend.Migration;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Auth;
using RensaioBackend.Services.Background;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Daily;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Images.Providers;
using RensaioBackend.Services.Import;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Jobs.Settings;
using RensaioBackend.Services.Opds;
using RensaioBackend.Services.Providers;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Scrobbling;
using RensaioBackend.Services.Scrobbling.Abstractions;
using RensaioBackend.Services.Search;
using RensaioBackend.Services.Series;
using RensaioBackend.Services.Settings;
using RensaioBackend.Services.Users;
using Microsoft.Extensions.DependencyInjection.Extensions;
using sun.net.www.http;
using System.Net.Http.Headers;

namespace RensaioBackend.Services
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddImportService(this IServiceCollection services)
        {
            services.TryAddScoped<SeriesScanner>();
            services.TryAddScoped<SeriesComparer>();
            services.TryAddScoped<ImportQueryService>();
            services.TryAddScoped<ImportCommandService>();
            services.TryAddScoped<UserImportService>();
            return services;
        }

        public static IServiceCollection AddRensaioJsonService(this IServiceCollection services)
        {
            // Singleton: shared per-file lock state across all services
            services.TryAddSingleton<RensaioJsonService>();
            return services;
        }

        public static IServiceCollection AddSeriesServices(this IServiceCollection services)
        {
            // Specialized series services
            services.TryAddScoped<SeriesQueryService>();
            services.TryAddScoped<SeriesCommandService>();
            services.TryAddScoped<SeriesProviderService>();
            services.TryAddScoped<SeriesArchiveService>();
            services.TryAddScoped<CadenceCalculationService>();
            
            // Series state sync service - central authority for rensaio.json sync
            services.TryAddScoped<SeriesStateService>();
            
            return services;
        }

        public static IServiceCollection AddJobServices(this IServiceCollection services)
        {
            // Core job services
            services.TryAddScoped<JobManagementService>();
            services.TryAddScoped<JobBusinessService>();
            services.TryAddScoped<JobExecutionService>();
            
            // Configuration and supporting services
            services.TryAddSingleton<JobsSettings>();
            services.TryAddScoped<JobHubReportService>();
            
            return services;
        }
        public static IServiceCollection AddHelperServices(this IServiceCollection services)
        {
            services.TryAddScoped<SettingsService>();

            services.AddScoped<IImageProvider, UrlImageProvider>();
            services.AddScoped<IImageProvider, ExtensionsImageProvider>();
            services.AddScoped<IImageProvider, StorageImageProvider>();
            services.TryAddScoped<ThumbCacheService>();
            services.TryAddScoped<IImageFactory, NetVipsImageFactory>();
            services.TryAddScoped<ArchiveHelperService>();
            services.TryAddScoped<DailyService>();
            services.TryAddScoped<Status.StatusEvaluationService>();
            services.TryAddScoped<MihonBridgeService>();
            services.TryAddScoped<MigrationService>();
            services.TryAddScoped<NouisanceFixer20ExtraLarge>();
            return services;
        }
        public static IServiceCollection AddScrobblingServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.TryAddScoped<ScrobblerTokenProtector>();
            services.TryAddScoped<ITokenStorageService, TokenStorageService>();
            services.TryAddScoped<ScrobblerProviderFactory>();
            services.TryAddScoped<TitleMatcher>();
            services.TryAddScoped<ScrobblerSyncService>();
            services.TryAddScoped<SeriesMatchingService>();

            // Register all IScrobblerProvider implementations.
            // NOTE: Must use AddScoped (not TryAddScoped) so each provider is registered.
            // ScrobblerProviderFactory resolves IEnumerable<IScrobblerProvider>.
            // Direct auth: Kitsu and MangaDex use password-based auth directly
            services.AddScoped<Scrobbling.Abstractions.IScrobblerProvider, Scrobbling.Providers.KitsuScrobblerProvider>();
            services.AddScoped<Scrobbling.Abstractions.IScrobblerProvider, Scrobbling.Providers.MangaDexScrobblerProvider>();

            // AniList and MyAnimeList use OAuth2 via the central OAuth proxy for authorization,
            // but call their respective APIs directly for search/tracking operations.
            services.AddScoped<Scrobbling.Abstractions.IScrobblerProvider, Scrobbling.Providers.AniListScrobblerProvider>();
            services.AddScoped<Scrobbling.Abstractions.IScrobblerProvider, Scrobbling.Providers.MyAnimeListScrobblerProvider>();

            // ComicVine still uses direct API key (no OAuth) - Do not support scrobbling, so commented
            //            services.AddScoped<Scrobbling.Abstractions.IScrobblerProvider, Scrobbling.Providers.ComicVineScrobblerProvider>();

            // Register HTTP clients
            services.AddHttpClient("Scrobbler_AniList", SetHttpClientHeaders);
            services.AddHttpClient("Scrobbler_MAL", SetHttpClientHeaders);
            services.AddHttpClient("Scrobbler_Kitsu", SetHttpClientHeaders);
            services.AddHttpClient("Scrobbler_MangaDex", SetHttpClientHeaders);

            return services;
        }
        private static void SetHttpClientHeaders(System.Net.Http.HttpClient client)
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Rensaio", "1.0"));
        }
        public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
        {
            services.TryAddSingleton<JobQueueHostedService>();
            services.TryAddSingleton<JobScheduledHostedService>();
            services.AddHostedService<DebouncedScrobblerSyncHostedService>();
            return services;
        }


        public static IServiceCollection AddProviderServices(this IServiceCollection services)
        {

            
            // Provider Services (SRP-focused)
            services.TryAddScoped<ProviderManagerService>();
            services.TryAddScoped<ProviderPreferencesService>();
            
            // Provider Cache and Storage
            services.TryAddScoped<ProviderCacheService>();
            
            return services;
        }

        public static IServiceCollection AddSearchServices(this IServiceCollection services)
        {
            // CQRS Search Services
            services.TryAddScoped<SearchQueryService>();
            services.TryAddScoped<SearchCommandService>();
            services.TryAddScoped<DiscoverySearchService>();
            services.TryAddSingleton<Search.Discovery.DiscoverySearchCoordinator>();
            services.TryAddSingleton<Search.Discovery.SnapshotSearchIndex>();
            services.TryAddSingleton<Search.Discovery.DiscoveryWorkerPool>();
            services.TryAddSingleton<Search.Discovery.DiscoverySourceHeaderRegistry>();
            services.TryAddSingleton<InteractiveDiscoveryGate>();
            services.TryAddSingleton<IContributionCheckpointStore, JsonContributionCheckpointStore>();
            services.TryAddSingleton<IContributionSink, LocalJsonContributionSink>();
            services.TryAddSingleton<IContributionWorkerController, ContributionWorkerController>();
            services.TryAddScoped<ContributionCollector>();

            // Contribution upload client: auth is a secret UUID in the query string, so the
            // factory's default request-URI logging must not run for this named client.
            services.AddHttpClient(Contributions.Upload.ContributionUploadClient.HttpClientName, client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    SetHttpClientHeaders(client);
                })
                .RemoveAllLoggers();
            services.TryAddSingleton<Contributions.Upload.IContributionUploadStateStore,
                Contributions.Upload.JsonContributionUploadStateStore>();
            services.TryAddScoped<Contributions.Upload.ContributionUploadClient>();
            services.TryAddScoped<Contributions.Upload.ContributionUploader>();

            // Contribution snapshot download client: the export files and the /key endpoint are
            // public, so no request-URI scrubbing is needed (nothing secret is ever sent).
            services.AddHttpClient(Contributions.Snapshot.ContributionSnapshotClient.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                SetHttpClientHeaders(client);
            });
            services.TryAddSingleton<Contributions.Snapshot.IContributionSnapshotStateStore,
                Contributions.Snapshot.JsonContributionSnapshotStateStore>();
            services.TryAddScoped<Contributions.Snapshot.ContributionSnapshotClient>();
            services.TryAddScoped<Contributions.Snapshot.ContributionSnapshotDownloader>();

            return services;
        }

        public static IServiceCollection AddDownloadServices(this IServiceCollection services)
        {
            // Download CQRS Services
            services.TryAddScoped<DownloadQueryService>();
            services.TryAddScoped<DownloadCommandService>();
            
            return services;
        }
        
        public static IServiceCollection AddMcpServices(this IServiceCollection services)
        {
            services.TryAddScoped<Mcp.McpToolService>();
            services.TryAddScoped<Mcp.Abstractions.IMcpPermissionService, Mcp.McpPermissionService>();
            return services;
        }

        public static IServiceCollection AddAuthServices(this IServiceCollection services)
        {
            services.TryAddScoped<PasswordService>();
            services.TryAddScoped<OpdsPathGenerator>();
            services.TryAddScoped<JwtTokenService>();
            services.TryAddScoped<UserInviteService>();
            services.TryAddScoped<UserQueryService>();
            services.TryAddScoped<UserCommandService>();
            return services;
        }

        public static IServiceCollection AddReadStateServices(this IServiceCollection services)
        {
            services.TryAddSingleton<ReadStateCacheService>();
            services.TryAddSingleton<ReadStateChangeNotifier>();
            services.TryAddScoped<ReadStateService>();
            return services;
        }

        public static IServiceCollection AddOpdsServices(this IServiceCollection services)
        {
            services.TryAddSingleton<HashCacheService>();
            services.TryAddSingleton<OpdsExtractionCoordinator>();
            services.TryAddSingleton<ClientCapabilitiesHelper>();
            services.TryAddScoped<OpdsImageService>();
            services.TryAddScoped<OpdsFeedService>();
            return services;
        }

    }
}

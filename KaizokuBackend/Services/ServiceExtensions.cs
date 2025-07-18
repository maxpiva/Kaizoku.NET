using KaizokuBackend.Services.Daily;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Settings;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Search;
using KaizokuBackend.Services.Series;
using KaizokuBackend.Services.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KaizokuBackend.Services
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddImportService(this IServiceCollection services)
        {
            services.TryAddScoped<SeriesScanner>();
            services.TryAddScoped<SeriesComparer>();
            // services.TryAddScoped<ImportService>();
            services.TryAddScoped<ImportQueryService>();
            services.TryAddScoped<ImportCommandService>();
            return services;
        }

        public static IServiceCollection AddSeriesServices(this IServiceCollection services)
        {
            // Specialized series services
            services.TryAddScoped<SeriesQueryService>();
            services.TryAddScoped<SeriesCommandService>();
            services.TryAddScoped<SeriesProviderService>();
            services.TryAddScoped<SeriesArchiveService>();
            
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
            services.TryAddScoped<EtagCacheService>();
            services.TryAddScoped<ContextProvider>();
            services.TryAddScoped<ArchiveHelperService>();
            services.TryAddScoped<DailyService>();
            return services;
        }


        public static IServiceCollection AddProviderServices(this IServiceCollection services)
        {

            
            // Provider Services (SRP-focused)
            services.TryAddScoped<ProviderQueryService>();
            services.TryAddScoped<ProviderInstallationService>();
            services.TryAddScoped<ProviderPreferencesService>();
            services.TryAddScoped<ProviderResourceService>();
            
            // Provider Cache and Storage
            services.TryAddScoped<ProviderCacheService>();
            
            // Legacy services (for gradual migration)
            services.TryAddScoped<ProviderManager>();
            
            return services;
        }

        public static IServiceCollection AddSearchServices(this IServiceCollection services)
        {
            // CQRS Search Services
            services.TryAddScoped<SearchQueryService>();
            services.TryAddScoped<SearchCommandService>();
            
            return services;
        }

        public static IServiceCollection AddDownloadServices(this IServiceCollection services)
        {
            // Download CQRS Services
            services.TryAddScoped<DownloadQueryService>();
            services.TryAddScoped<DownloadCommandService>();
            
            return services;
        }
    }
}

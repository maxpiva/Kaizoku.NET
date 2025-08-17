using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Scaffolding;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace KaizokuBackend.Services.Background
{
    public class StartupHostedService : IHostedService, IDisposable
    {
        private readonly ILogger<StartupHostedService> _logger;
        private readonly string _runtimeDir;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SuwayomiHostedService _service;
        private bool _disposed = false;
        private bool _do_not_spawn_suwayomi = false;

        public StartupHostedService(ILogger<StartupHostedService> logger, 
            IServiceScopeFactory scopeFactory,
            IConfiguration config, SuwayomiHostedService suwayomi)
        {
            _logger = logger;
            _service = suwayomi;
            _scopeFactory = scopeFactory;
            _runtimeDir = config["runtimeDirectory"] ?? "";
            _do_not_spawn_suwayomi = config.GetValue<bool>("Suwayomi:UseCustomApi", false);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // Use a timeout for disposal
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    StopAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during disposal of StartupHostedService");
                }
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public async Task<bool> CheckStorageStatusAsync(AppDbContext db, Models.Settings settings, IHostApplicationLifetime lifetime, CancellationToken token = default)
        {

            Models.Database.Series? series = await db.Series.AsNoTracking().OrderBy(a=>a.Id).FirstOrDefaultAsync(token).ConfigureAwait(false);

            bool hasArchiveFiles = ArchiveHelperService.ContainsArchiveFilesRecursive(settings.StorageFolder);
            if (!hasArchiveFiles && series!=null)
            {
                _logger.LogError("No archive files found in the storage folder. But database has content, shutting down...");
                lifetime.StopApplication();
                return false;
            }
            else if (hasArchiveFiles && series == null)
            {
                //We have archive files, but no series in the database, we start the wizard setup
                settings.IsWizardSetupComplete = false;
                settings.WizardSetupStepCompleted = 0;
            }
            else
            {
                // We have archive files and series in the database, or everything is empty, we can proceed
                settings.IsWizardSetupComplete = true;
                settings.WizardSetupStepCompleted = 0;
            }

            return true;
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {

                // Start Suwayomi service
                if (!_do_not_spawn_suwayomi)
                    await _service.StartAsync(_runtimeDir, cancellationToken).ConfigureAwait(false);
                
                // Initialize other services
                using var scope = _scopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var providerCacheService = scope.ServiceProvider.GetRequiredService<ProviderCacheService>();
                
                // Load settings
                Models.Settings settings = await settingsService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                settingsService.SetThreadSettings(settings);
                await settingsService.SetTimesSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
                IHostApplicationLifetime lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
                JobManagementService jobManagement = scope.ServiceProvider.GetRequiredService<JobManagementService>();
                _logger.LogInformation("Checking Storage folder Status...");
                bool save = await CheckStorageStatusAsync(db, settings, lifetime, cancellationToken).ConfigureAwait(false);
                if (save)
                    await settingsService.SaveSettingsAsync(settings, true, cancellationToken).ConfigureAwait(false);
                // Cache providers
                _logger.LogInformation("Syncing Mihon Extensions Preferences, this could take a little...");
                await providerCacheService.RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
                var jobs = await jobManagement.GetRecurringJobsByTypeAsync(JobType.DailyUpdate, cancellationToken).ConfigureAwait(false);
                if (jobs.Count == 0)
                {
                    await jobManagement.ScheduleRecurringJobAsync(JobType.DailyUpdate, (string?)null,null, null,false, TimeSpan.FromDays(1),Priority.Normal, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Startup Hosted Service");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                
                // Stop Suwayomi service gracefully
                await _service.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Startup Hosted Service");
                throw;
            }
        }
    }
}
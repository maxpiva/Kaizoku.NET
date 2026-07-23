using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Migration;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Providers;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.ComponentModel;

namespace RensaioBackend.Services.Background
{
    public class StartupHostedService : IHostedService, IDisposable
    {
        private readonly NouisanceFixer20ExtraLarge _fixes;
        private readonly ILogger<StartupHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly List<Task> _workerTasks = new();
        private CancellationTokenSource? _workerCts;
        private bool _disposed = false;

        public StartupHostedService(ILogger<StartupHostedService> logger, 
            IServiceScopeFactory scopeFactory,
            NouisanceFixer20ExtraLarge fixes,
            IConfiguration config)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _fixes = fixes;
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

        public async Task<bool> CheckStorageStatusAsync(AppDbContext db, SettingsDto settings, IHostApplicationLifetime lifetime, CancellationToken token = default)
        {

            Models.Database.SeriesEntity? series = await db.Series.AsNoTracking().OrderBy(a=>a.Id).FirstOrDefaultAsync(token).ConfigureAwait(false);

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

                using var scope = _scopeFactory.CreateScope();
                // Wait for BridgeHost (background) to fully initialize the bridge.
                // This awaits the full sequence: InitAndroidAppAsync (Kotlin/Android
                // compat layer, CEF, SettingsConfig) followed by BridgeManager.InitializeAsync
                // (repository discovery, extension validation, recompilation).
                // Once the gate completes, all bridge services are safe to use.
                var mihon = scope.ServiceProvider.GetRequiredService<IBridgeManager>();
                await mihon.InitializationCompleted.WaitAsync(cancellationToken);
                _logger.LogInformation("Bridge initialization gate passed — bridge is ready.");


               

                //Run migration if needed
                var migration = scope.ServiceProvider.GetRequiredService<MigrationService>();
                await migration.RunAsync(cancellationToken).ConfigureAwait(false);


                // Initialize other services
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var providerCacheService = scope.ServiceProvider.GetRequiredService<ProviderCacheService>();
                
                // Load settings
                SettingsDto settings = await settingsService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                settingsService.SetThreadSettings(settings);
                await settingsService.SetTimesSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
                //await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
                await _fixes.FixThumbnailsOfSeriesWithMissingThumbnailsAsync(cancellationToken).ConfigureAwait(false);

                // Fix: Ensure IsLocal = true for all providers without MihonProviderId that aren't Unknown
                List<SeriesProviderEntity> localProviderFixes = await db.SeriesProviders
                    .Where(a => string.IsNullOrEmpty(a.MihonProviderId) && !a.IsUnknown && !a.IsLocal)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                if (localProviderFixes.Count > 0)
                {
                    _logger.LogInformation("Fixing {Count} SeriesProviders with missing IsLocal flag", localProviderFixes.Count);
                    foreach (var p in localProviderFixes)
                        p.IsLocal = true;
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                scope.ServiceProvider.GetRequiredService<ReadStateService>().PrefetchCache(await db.Series.ToListAsync(cancellationToken).ConfigureAwait(false));

                IHostApplicationLifetime lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
                JobManagementService jobManagement = scope.ServiceProvider.GetRequiredService<JobManagementService>();
                _logger.LogInformation("Checking Storage folder Status...");
                bool save = await CheckStorageStatusAsync(db, settings, lifetime, cancellationToken).ConfigureAwait(false);
                if (save)
                    await settingsService.SaveSettingsAsync(settings, true, cancellationToken).ConfigureAwait(false);
                // Cache providers
                _logger.LogInformation("Syncing Mihon Extensions Preferences.");
                await providerCacheService.RefreshCacheAsync(false, cancellationToken).ConfigureAwait(false);
                var jobs = await jobManagement.GetRecurringJobsByTypeAsync(JobType.DailyUpdate, cancellationToken).ConfigureAwait(false);
                if (jobs.Count == 0)
                {
                    await jobManagement.ScheduleRecurringJobAsync(JobType.DailyUpdate, (string?)null,null, null,false, TimeSpan.FromDays(1),Priority.Normal, cancellationToken).ConfigureAwait(false);
                }
                // Schedule health status check job (runs every hour)
                var statusJobs = await jobManagement.GetRecurringJobsByTypeAsync(JobType.StatusCheck, cancellationToken).ConfigureAwait(false);
                if (statusJobs.Count == 0)
                {
                    await jobManagement.ScheduleRecurringJobAsync(JobType.StatusCheck, (string?)null, null, null, false, TimeSpan.FromHours(1), Priority.Normal, cancellationToken).ConfigureAwait(false);
                }

                // Schedule daily series verification job
                var verifyJobs = await jobManagement.GetRecurringJobsByTypeAsync(JobType.VerifyAllSeries, cancellationToken).ConfigureAwait(false);
                if (verifyJobs.Count == 0)
                {
                    _logger.LogInformation("Scheduling daily series verification job...");
                    await jobManagement.ScheduleRecurringJobAsync(JobType.VerifyAllSeries, (string?)null, null, null, false, TimeSpan.FromDays(1), Priority.Low, cancellationToken).ConfigureAwait(false);
                }

                // Enqueue an immediate verification run at startup.
                // Use matching key "VerifyAllSeries" so the dedup check in EnqueueJobAsIsAsync
                // prevents a double-run if the scheduled job already enqueued one.
                _logger.LogWarning("Starting initial series integrity verification at startup. This may take a while depending on the library size and archive file sizes.");
                await jobManagement.EnqueueJobAsync(JobType.VerifyAllSeries, (string?)null, Priority.Low, "VerifyAllSeries", null, null, "Default", cancellationToken).ConfigureAwait(false);

                _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var workerToken = _workerCts.Token;
                _workerTasks.Add(StartWorker<JobQueueHostedService>(workerToken));
                _workerTasks.Add(StartWorker<JobScheduledHostedService>(workerToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Startup Hosted Service");
                throw;
            }
        }

        private Task StartWorker<TWorker>(CancellationToken workerToken) where TWorker : IWorkerService
        {
            var task = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<TWorker>();
                try
                {
                    await worker.ExecuteAsync(workerToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker crashed");
                }
            });
            return task;
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_workerCts == null)
                return;

            _workerCts.Cancel();

            try
            {
                // Use a dedicated 30-second timeout independent of the host's cancellation token
                // so workers have time to drain in-flight work even if the host cancels early.
                await Task.WhenAll(_workerTasks).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                _logger.LogInformation("All background workers stopped gracefully.");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Background workers did not stop within the 30-second shutdown timeout.");
            }
            catch (OperationCanceledException)
            {
                // Swallow: host is shutting down, some workers may have been cancelled
            }
            finally
            {
                _workerCts.Dispose();
                _workerCts = null;
                _workerTasks.Clear();
            }
        }
    }
}
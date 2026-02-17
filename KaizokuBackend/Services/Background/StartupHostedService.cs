using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Migration;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.ComponentModel;

namespace KaizokuBackend.Services.Background
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
                //Initialize Mihon Bridge
                var mihon = scope.ServiceProvider.GetRequiredService<IBridgeManager>();
                await mihon.InitializeAsync(cancellationToken);


              

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
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
                await _fixes.FixThumbnailsOfSeriesWithMissingThumbnailsAsync(cancellationToken).ConfigureAwait(false);

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
        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_workerCts == null)
                return Task.CompletedTask;

            _workerCts.Cancel();

            return Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(_workerTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // host is shutting down, swallow cancellation
                }
                finally
                {
                    _workerCts.Dispose();
                    _workerCts = null;
                    _workerTasks.Clear();
                }
            }, CancellationToken.None);
        }
    }
}
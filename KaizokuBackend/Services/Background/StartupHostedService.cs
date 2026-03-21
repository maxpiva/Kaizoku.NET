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
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                await EnsureAuthTablesAsync(db, cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
                await _fixes.FixThumbnailsOfSeriesWithMissingThumbnailsAsync(cancellationToken).ConfigureAwait(false);

                IHostApplicationLifetime lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
                JobManagementService jobManagement = scope.ServiceProvider.GetRequiredService<JobManagementService>();
                _logger.LogInformation("Checking Storage folder Status...");
                bool save = await CheckStorageStatusAsync(db, settings, lifetime, cancellationToken).ConfigureAwait(false);
                if (save)
                    await settingsService.SaveSettingsAsync(settings, true, cancellationToken).ConfigureAwait(false);
                // Cache providers — wrapped in try-catch so broken extensions don't prevent startup
                _logger.LogInformation("Syncing Mihon Extensions Preferences.");
                try
                {
                    await providerCacheService.RefreshCacheAsync(false, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Provider cache refresh failed during startup. " +
                        "Some extensions may be unavailable. The app will continue with a degraded provider set.");
                }
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

        /// <summary>
        /// Ensures auth/multi-user tables exist for existing databases that were created before the multi-user feature.
        /// For new installations, EnsureCreatedAsync already creates these tables.
        /// </summary>
        private async Task EnsureAuthTablesAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            try
            {
                // Check if Users table already exists
                var tableExists = false;
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT 1 FROM \"Users\" LIMIT 1;", cancellationToken).ConfigureAwait(false);
                    tableExists = true;
                }
                catch
                {
                    tableExists = false;
                }

                if (tableExists)
                    return;

                _logger.LogInformation("Creating auth tables for multi-user support...");

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""Users"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""Username"" TEXT NOT NULL COLLATE BINARY,
                        ""Email"" TEXT NOT NULL COLLATE BINARY,
                        ""DisplayName"" TEXT NOT NULL COLLATE BINARY,
                        ""PasswordHash"" TEXT NOT NULL COLLATE BINARY,
                        ""Salt"" TEXT NOT NULL COLLATE BINARY,
                        ""Role"" INTEGER NOT NULL,
                        ""AvatarPath"" TEXT COLLATE BINARY,
                        ""CreatedAt"" TEXT NOT NULL,
                        ""UpdatedAt"" TEXT NOT NULL,
                        ""LastLoginAt"" TEXT,
                        ""IsActive"" INTEGER NOT NULL
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_User_Username"" ON ""Users"" (""Username"");", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_User_Email"" ON ""Users"" (""Email"");", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""UserPermissions"" (
                        ""UserId"" TEXT NOT NULL PRIMARY KEY,
                        ""CanViewLibrary"" INTEGER NOT NULL,
                        ""CanRequestSeries"" INTEGER NOT NULL,
                        ""CanAddSeries"" INTEGER NOT NULL,
                        ""CanEditSeries"" INTEGER NOT NULL,
                        ""CanDeleteSeries"" INTEGER NOT NULL,
                        ""CanManageDownloads"" INTEGER NOT NULL,
                        ""CanViewQueue"" INTEGER NOT NULL,
                        ""CanBrowseSources"" INTEGER NOT NULL,
                        ""CanViewNSFW"" INTEGER NOT NULL,
                        ""CanManageRequests"" INTEGER NOT NULL,
                        ""CanManageJobs"" INTEGER NOT NULL,
                        ""CanViewStatistics"" INTEGER NOT NULL,
                        FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""UserSessions"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""UserId"" TEXT NOT NULL,
                        ""RefreshToken"" TEXT NOT NULL COLLATE BINARY,
                        ""ExpiresAt"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        ""IpAddress"" TEXT COLLATE BINARY,
                        ""UserAgent"" TEXT COLLATE BINARY,
                        ""IsRevoked"" INTEGER NOT NULL,
                        FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_UserSession_RefreshToken"" ON ""UserSessions"" (""RefreshToken"");", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_UserSession_UserId"" ON ""UserSessions"" (""UserId"");", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_UserSession_UserId_IsRevoked"" ON ""UserSessions"" (""UserId"", ""IsRevoked"");", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""UserPreferences"" (
                        ""UserId"" TEXT NOT NULL PRIMARY KEY,
                        ""Theme"" TEXT NOT NULL COLLATE BINARY,
                        ""DefaultLanguage"" TEXT NOT NULL COLLATE BINARY,
                        ""CardSize"" TEXT NOT NULL COLLATE BINARY,
                        ""NsfwVisibility"" INTEGER NOT NULL,
                        FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""PermissionPresets"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""Name"" TEXT NOT NULL COLLATE BINARY,
                        ""CreatedByUserId"" TEXT NOT NULL,
                        ""IsDefault"" INTEGER NOT NULL,
                        ""CanViewLibrary"" INTEGER NOT NULL,
                        ""CanRequestSeries"" INTEGER NOT NULL,
                        ""CanAddSeries"" INTEGER NOT NULL,
                        ""CanEditSeries"" INTEGER NOT NULL,
                        ""CanDeleteSeries"" INTEGER NOT NULL,
                        ""CanManageDownloads"" INTEGER NOT NULL,
                        ""CanViewQueue"" INTEGER NOT NULL,
                        ""CanBrowseSources"" INTEGER NOT NULL,
                        ""CanViewNSFW"" INTEGER NOT NULL,
                        ""CanManageRequests"" INTEGER NOT NULL,
                        ""CanManageJobs"" INTEGER NOT NULL,
                        ""CanViewStatistics"" INTEGER NOT NULL,
                        FOREIGN KEY (""CreatedByUserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PermissionPreset_Name"" ON ""PermissionPresets"" (""Name"");", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_PermissionPreset_IsDefault"" ON ""PermissionPresets"" (""IsDefault"");", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""InviteLinks"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""Code"" TEXT NOT NULL COLLATE BINARY,
                        ""CreatedByUserId"" TEXT NOT NULL,
                        ""ExpiresAt"" TEXT NOT NULL,
                        ""MaxUses"" INTEGER NOT NULL,
                        ""UsedCount"" INTEGER NOT NULL,
                        ""PermissionPresetId"" TEXT,
                        ""IsActive"" INTEGER NOT NULL,
                        FOREIGN KEY (""CreatedByUserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                        FOREIGN KEY (""PermissionPresetId"") REFERENCES ""PermissionPresets"" (""Id"") ON DELETE SET NULL
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_InviteLink_Code"" ON ""InviteLinks"" (""Code"");", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_InviteLink_IsActive"" ON ""InviteLinks"" (""IsActive"");", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""MangaRequests"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""RequestedByUserId"" TEXT NOT NULL,
                        ""Title"" TEXT NOT NULL COLLATE BINARY,
                        ""Description"" TEXT COLLATE BINARY,
                        ""ThumbnailUrl"" TEXT COLLATE BINARY,
                        ""ProviderData"" TEXT COLLATE BINARY,
                        ""Status"" INTEGER NOT NULL,
                        ""ReviewedByUserId"" TEXT,
                        ""ReviewedAt"" TEXT,
                        ""ReviewNote"" TEXT COLLATE BINARY,
                        ""CreatedAt"" TEXT NOT NULL,
                        FOREIGN KEY (""RequestedByUserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                        FOREIGN KEY (""ReviewedByUserId"") REFERENCES ""Users"" (""Id"") ON DELETE SET NULL
                    );", cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_MangaRequest_Status"" ON ""MangaRequests"" (""Status"");", cancellationToken).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(@"CREATE INDEX IF NOT EXISTS ""IX_MangaRequest_RequestedByUserId"" ON ""MangaRequests"" (""RequestedByUserId"");", cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Auth tables created successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating auth tables");
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
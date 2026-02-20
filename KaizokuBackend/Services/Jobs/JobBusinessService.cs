using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Settings;
using System.Text.Json;

namespace KaizokuBackend.Services.Jobs
{
    /// <summary>
    /// Service containing business logic for different job types
    /// </summary>
    public class JobBusinessService
    {
        private readonly JobManagementService _jobManagement;
        private readonly SettingsService _settings;
        private readonly ILogger<JobBusinessService> _logger;

        public JobBusinessService(JobManagementService jobManagement, SettingsService settings, 
            ILogger<JobBusinessService> logger)
        {
            _jobManagement = jobManagement;
            _settings = settings;
            _logger = logger;
        }

        #region Series Provider Job Management

        public async Task ManageSeriesProviderJobAsync(SeriesProviderEntity provider, bool runNow = false, 
            bool forceDisable = false, CancellationToken token = default)
        {
            string groupKey = BuildProviderGroupKey(provider);
            
            if (provider.IsDisabled || provider.IsUninstalled || forceDisable)
            {
                await _jobManagement.DisableRecurringJobAsync(JobType.GetChapters, provider.Id.ToString(), token)
                    .ConfigureAwait(false);
            }
            else
            {
                await _jobManagement.ScheduleRecurringJobAsync(JobType.GetChapters, provider.Id, 
                    provider.Id.ToString(), groupKey, runNow, priority: Priority.Low, token: token)
                    .ConfigureAwait(false);
            }
        }

        public async Task DeleteSeriesProviderJobAsync(SeriesProviderEntity provider, CancellationToken token = default)
        {
            await _jobManagement.DeleteRecurringJobAsync(JobType.GetChapters, provider.Id.ToString(), token)
                .ConfigureAwait(false);
        }

        #endregion

        #region Extension Management

        public async Task ManageExtensionUpdatesAsync(bool enable, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            string groupKey = nameof(JobType.UpdateExtensions);
            
            if (!enable)
            {
                await _jobManagement.DisableRecurringJobAsync(JobType.UpdateExtensions, groupKey, token)
                    .ConfigureAwait(false);
            }
            else
            {
                await _jobManagement.ScheduleRecurringJobAsync(JobType.UpdateExtensions, groupKey, 
                    groupKey, groupKey, false, settings.ExtensionsCheckForUpdateSchedule, Priority.High, token)
                    .ConfigureAwait(false);
            }
        }

        #endregion

        #region Source Management

        public async Task ManageSourceJobAsync(ProviderStorageEntity provider, bool enable, bool runNow = false, 
            CancellationToken token = default)
        {
            string groupKey = BuildSourceGroupKey(provider);
            string mihonProviderId = provider.MihonProviderId;

            if (enable)
            {
                await _jobManagement.ScheduleRecurringJobAsync(JobType.GetLatest, JsonSerializer.Serialize(mihonProviderId), mihonProviderId,
                    groupKey, runNow, priority: Priority.Low, token: token)
                    .ConfigureAwait(false);
            }
            else
            {
                await _jobManagement.DisableRecurringJobAsync(JobType.GetLatest, mihonProviderId, token)
                    .ConfigureAwait(false);
            }
        }

        #endregion

        #region Job Status

        public async Task<bool?> GetJobStatusAsync(JobType jobType, string key, CancellationToken token = default)
        {
            return await _jobManagement.GetRecurringJobStatusAsync(jobType, key, token).ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        private static string BuildProviderGroupKey(SeriesProviderEntity provider)
        {
            return $"{provider.Provider}|{provider.Language}|{provider.Scanlator ?? ""}";
        }

        private static string BuildSourceGroupKey(ProviderStorageEntity provider)
        {
            return $"{provider.Name}|{provider.Language}";
        }

        #endregion
    }
}
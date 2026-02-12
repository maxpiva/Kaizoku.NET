using KaizokuBackend.Data;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Background;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Settings;
using KaizokuBackend.Services.Providers;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.Reflection;

namespace KaizokuBackend.Services.Settings
{
    public class SettingsService
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;
        private readonly SuwayomiClient _client;
        private readonly IServiceScopeFactory _prov;

        private static Models.Settings? _settings;

        public SettingsService(IConfiguration config, IServiceScopeFactory prov, SuwayomiClient client, AppDbContext db)
        {
            _config = config;
            _client = client;
            _db = db;
            _prov = prov;

        }


        public async Task<string[]> GetAvailableLanguagesAsync(CancellationToken token = default)
        {
            using (var scope = _prov.CreateScope())
            {
                ProviderCacheService cache = scope.ServiceProvider.GetRequiredService<ProviderCacheService>();
                var all = await cache.GetCachedProvidersAsync(token).ConfigureAwait(false);
                List<string> languages = all.SelectMany(p => p.Mappings).Select(s => s.Source?.Lang.ToLowerInvariant() ?? "")
                    .Distinct()
                    .OrderBy(a => a).ToList();
                languages.Remove("all");
                return languages.ToArray();
            }
        }


        private static List<Setting> Serialize(EditableSettings editableSettings)
        {
            List<Setting> serializedSettings = new List<Setting>();
            List<PropertyInfo> props = typeof(EditableSettings).GetProperties().ToList();
            foreach (PropertyInfo p in props)
            {
                Setting setting = new Setting
                {
                    Name = p.Name,

                };
                switch (p.PropertyType.Name.ToLowerInvariant())
                {
                    case "string":
                        setting.Value = p.GetValue(editableSettings)?.ToString() ?? string.Empty;
                        break;
                    case "string[]":
                        string[] array = (string[])p.GetValue(editableSettings)!;
                        setting.Value = string.Join('|', array);
                        break;
                    case "int32":
                        setting.Value = p.GetValue(editableSettings)?.ToString() ?? "0";
                        break;
                    case "boolean":
                        setting.Value = p.GetValue(editableSettings)?.ToString() ?? "false";
                        break;
                    case "timespan":
                        setting.Value = ((TimeSpan)(p.GetValue(editableSettings) ?? TimeSpan.Zero)).ToString();
                        break;
                    case "datetime":
                        setting.Value = ((DateTime)(p.GetValue(editableSettings) ?? new DateTime(0,1,1,4,0,0))).ToString("o"); // ISO 8601 format
                        break;
                    default:
                        if (p.PropertyType.IsEnum)
                            setting.Value = p.GetValue(editableSettings)?.ToString() ?? string.Empty;
                        break;
                }
                serializedSettings.Add(setting);
            }
            return serializedSettings;
        }

        private static (bool, EditableSettings) Deserialize(List<Setting> settings, EditableSettings defaultValues) 
        {
            bool needSave = false;
            List<PropertyInfo> props = typeof(EditableSettings).GetProperties().ToList();
            EditableSettings newEditableSettings = new EditableSettings();
            foreach (PropertyInfo p in props)
            {
                string propType = p.PropertyType.Name.ToLowerInvariant();
                Setting? setting = settings.FirstOrDefault(s => s.Name == p.Name);
                if (setting == null)
                {
                    string value;
                    switch (propType)
                    {
                        case "string[]":
                            string[] split = p.GetValue(defaultValues) as string[] ?? [];
                            value = string.Join('|', split);
                            break;
                        default:
                            value = p.GetValue(defaultValues)?.ToString() ?? string.Empty;
                            break;
                    }

                    setting = new Setting
                    {
                        Name = p.Name, 
                        Value = value
                    };
                    needSave = true;
                }

                switch (propType)
                {
                    case "string":
                        p.SetValue(newEditableSettings, setting.Value);
                        break;
                    case "string[]":
                        string[] split = setting.Value.Split('|');
                        p.SetValue(newEditableSettings, split);
                        break;
                    case "int32":
                        p.SetValue(newEditableSettings, int.TryParse(setting.Value, out int intValue) ? intValue : 0);
                        break;
                    case "boolean":
                        p.SetValue(newEditableSettings, bool.TryParse(setting.Value, out bool boolValue) ? boolValue : false);
                        break;
                    case "timespan":
                        p.SetValue(newEditableSettings, TimeSpan.TryParse(setting.Value, out TimeSpan timeSpanValue) ? timeSpanValue : TimeSpan.Zero);
                        break;
                    case "datetime":
                        p.SetValue(newEditableSettings, DateTime.TryParse(setting.Value, out DateTime dateTimeValue) ? dateTimeValue : DateTime.MinValue);
                        break;
                    default:
                        if (p.PropertyType.IsEnum)
                            p.SetValue(newEditableSettings, Enum.TryParse(p.PropertyType, setting.Value, out var enumValue) ? enumValue : p.GetValue(defaultValues));
                        break;
                }
            }
            return (needSave, newEditableSettings);
        }
        private static string JoinAndSortArray(string[] array)
        {
            return string.Join('|', array.OrderBy(a => a));
        }
        private async Task SaveToSuwayomiAsync(EditableSettings set, bool force = false, CancellationToken token = default)
        {
            Dictionary<string, object> parametersToSaveOnSuwayomi = new Dictionary<string, object>();
            if (force || _settings == null || _settings.NumberOfSimultaneousDownloads != set.NumberOfSimultaneousDownloads)
            {
                parametersToSaveOnSuwayomi.Add("maxSourcesInParallel", set.NumberOfSimultaneousDownloads);
            }
            if (force || _settings == null || JoinAndSortArray(_settings.MihonRepositories) != JoinAndSortArray(set.MihonRepositories))
            {
                parametersToSaveOnSuwayomi.Add("mihonRepositories", set.MihonRepositories);
            }
            if (force || _settings == null || _settings.FlareSolverrEnabled != set.FlareSolverrEnabled)
            {
                parametersToSaveOnSuwayomi.Add("flareSolverrEnabled", set.FlareSolverrEnabled);
            }
            if (force || _settings == null || _settings.FlareSolverrUrl != set.FlareSolverrUrl)
            {
                parametersToSaveOnSuwayomi.Add("flareSolverrUrl", set.FlareSolverrUrl);
            }
            if (force || _settings == null || _settings.FlareSolverrTimeout != set.FlareSolverrTimeout)
            {
                parametersToSaveOnSuwayomi.Add("flareSolverrTimeout", set.FlareSolverrTimeout.TotalSeconds);
            }
            if (force || _settings == null || _settings.FlareSolverrSessionTtl != set.FlareSolverrSessionTtl)
            {
                parametersToSaveOnSuwayomi.Add("flareSolverrSessionTtl", set.FlareSolverrSessionTtl.TotalMinutes);
            }
            if (force || _settings == null || _settings.FlareSolverrAsResponseFallback != set.FlareSolverrAsResponseFallback)
            {
                parametersToSaveOnSuwayomi.Add("flareSolverrAsResponseFallback", set.FlareSolverrAsResponseFallback);
            }
            if (parametersToSaveOnSuwayomi.Count > 0)
            {
                await _client.SetServerSettingsAsync(parametersToSaveOnSuwayomi, token).ConfigureAwait(false);
            }
        }

        public void SetThreadSettings(EditableSettings set)
        {
            using (var scope = _prov.CreateScope())
            {
                var settings = scope.ServiceProvider.GetRequiredService<JobsSettings>();
                settings.SetQueueSettings(JobQueues.Downloads, set.NumberOfSimultaneousDownloads, 20, set.NumberOfSimultaneousDownloadsPerProvider, set.ChapterDownloadFailRetryTime);
                settings.SetQueueSettings(JobQueues.Default, 10, set.ChapterDownloadFailRetries, 10, set.ChapterDownloadFailRetryTime);
            }
        }

        public async Task SetTimesSettingsAsync(EditableSettings set, CancellationToken token = default)
        {
            using (var scope = _prov.CreateScope())
            {
                var settings = scope.ServiceProvider.GetRequiredService<JobsSettings>();
                var jobManagment = scope.ServiceProvider.GetRequiredService<JobManagementService>();
                settings.JobTimes[JobType.GetChapters] = set.PerTitleUpdateSchedule;
                settings.JobTimes[JobType.GetLatest] = set.PerSourceUpdateSchedule;
                settings.JobTimes[JobType.UpdateExtensions] = set.ExtensionsCheckForUpdateSchedule;
                await jobManagment.SetRecurringTimeAsync(JobType.GetChapters, set.PerTitleUpdateSchedule, token).ConfigureAwait(false);
                await jobManagment.SetRecurringTimeAsync(JobType.GetLatest, set.PerSourceUpdateSchedule, token).ConfigureAwait(false);
                await jobManagment.SetRecurringTimeAsync(JobType.UpdateExtensions, set.ExtensionsCheckForUpdateSchedule, token).ConfigureAwait(false);
            }
        }

        public async Task SaveSettingsAsync(EditableSettings set, bool force = false, CancellationToken token = default)
        {
            if (set.NumberOfSimultaneousDownloads != _settings?.NumberOfSimultaneousDownloads ||
                set.ChapterDownloadFailRetries != _settings?.ChapterDownloadFailRetries ||
                set.ChapterDownloadFailRetryTime != _settings?.ChapterDownloadFailRetryTime || 
                set.NumberOfSimultaneousDownloadsPerProvider != _settings?.NumberOfSimultaneousDownloadsPerProvider
                )
            {
                SetThreadSettings(set);
            }
            if (set.PerTitleUpdateSchedule != _settings?.PerTitleUpdateSchedule ||
                set.PerSourceUpdateSchedule != _settings?.PerSourceUpdateSchedule || set.ExtensionsCheckForUpdateSchedule!=_settings?.ExtensionsCheckForUpdateSchedule)
            {
                await SetTimesSettingsAsync(set, token).ConfigureAwait(false);
            }
            await SaveToSuwayomiAsync(set, force, token).ConfigureAwait(false);
            List<Setting> dbsettings = await _db.Settings.ToListAsync(token).ConfigureAwait(false);
            List<Setting> newSettings = Serialize(set);
            bool needSave = false;
            foreach (Setting setting in newSettings)
            {
                Setting? dbsetting = dbsettings.FirstOrDefault(s => s.Name == setting.Name);
                if (dbsetting == null)
                {
                    _db.Settings.Add(setting);
                    needSave = true;
                }
                else if (dbsetting.Value != setting.Value)
                {
                    dbsetting.Value = setting.Value;
                    needSave = true;
                }
            }            if (needSave)
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _settings = GetFromEditableSettings(set);
        }
        
        public async Task SaveSettingsAsync(Models.Settings settings, bool force, CancellationToken token = default)
        {
            // Convert Settings to EditableSettings since the existing logic works with EditableSettings
            var editableSettings = new EditableSettings
            {
                PreferredLanguages = settings.PreferredLanguages,
                MihonRepositories = settings.MihonRepositories,
                NumberOfSimultaneousDownloads = settings.NumberOfSimultaneousDownloads,
                NumberOfSimultaneousDownloadsPerProvider = settings.NumberOfSimultaneousDownloadsPerProvider,
                NumberOfSimultaneousSearches = settings.NumberOfSimultaneousSearches,
                ChapterDownloadFailRetryTime = settings.ChapterDownloadFailRetryTime,
                ChapterDownloadFailRetries = settings.ChapterDownloadFailRetries,
                PerTitleUpdateSchedule = settings.PerTitleUpdateSchedule,
                PerSourceUpdateSchedule = settings.PerSourceUpdateSchedule,
                ExtensionsCheckForUpdateSchedule = settings.ExtensionsCheckForUpdateSchedule,
                CategorizedFolders = settings.CategorizedFolders,
                Categories = settings.Categories,
                FlareSolverrEnabled = settings.FlareSolverrEnabled,
                FlareSolverrUrl = settings.FlareSolverrUrl,
                FlareSolverrTimeout = settings.FlareSolverrTimeout,
                FlareSolverrSessionTtl = settings.FlareSolverrSessionTtl,
                FlareSolverrAsResponseFallback = settings.FlareSolverrAsResponseFallback,
                IsWizardSetupComplete = settings.IsWizardSetupComplete,
                WizardSetupStepCompleted = settings.WizardSetupStepCompleted,
                NsfwVisibility = settings.NsfwVisibility
            };

            await SaveSettingsAsync(editableSettings, force, token).ConfigureAwait(false);
        }
        
        public Models.Settings GetFromEditableSettings(EditableSettings ed)
        {
            Models.Settings set = new Models.Settings
            {
                PreferredLanguages = ed.PreferredLanguages,
                MihonRepositories = ed.MihonRepositories,
                NumberOfSimultaneousDownloads = ed.NumberOfSimultaneousDownloads,
                NumberOfSimultaneousDownloadsPerProvider = ed.NumberOfSimultaneousDownloadsPerProvider,
                NumberOfSimultaneousSearches = ed.NumberOfSimultaneousSearches,
                ChapterDownloadFailRetryTime = ed.ChapterDownloadFailRetryTime,
                ChapterDownloadFailRetries = ed.ChapterDownloadFailRetries,
                PerTitleUpdateSchedule = ed.PerTitleUpdateSchedule,
                PerSourceUpdateSchedule = ed.PerSourceUpdateSchedule,
                ExtensionsCheckForUpdateSchedule = ed.ExtensionsCheckForUpdateSchedule,
                CategorizedFolders = ed.CategorizedFolders,
                Categories = ed.Categories,
                FlareSolverrEnabled = ed.FlareSolverrEnabled,
                FlareSolverrUrl = ed.FlareSolverrUrl,
                FlareSolverrTimeout = ed.FlareSolverrTimeout,
                FlareSolverrSessionTtl = ed.FlareSolverrSessionTtl,
                FlareSolverrAsResponseFallback = ed.FlareSolverrAsResponseFallback,
                IsWizardSetupComplete = ed.IsWizardSetupComplete,
                WizardSetupStepCompleted = ed.WizardSetupStepCompleted,
                NsfwVisibility = ed.NsfwVisibility
            };
            set.StorageFolder = _config["StorageFolder"] ?? string.Empty;
            return set;
        }
        public async ValueTask<Models.Settings> GetSettingsAsync(CancellationToken token = default)
        {
            if (_settings != null)
                return _settings;
            Models.Settings firstTimeEditableSettings = new Models.Settings();
            _config.Bind("FirstTimeSettings", firstTimeEditableSettings);
            List<Setting> settings = await _db.Settings.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            bool needSave;
            if (settings.Count == 0)
            {
                _settings = firstTimeEditableSettings;
                needSave = true;
            }
            else
            {
                (needSave, EditableSettings set) = Deserialize(settings, firstTimeEditableSettings);
                _settings = GetFromEditableSettings(set);
            }
            if (needSave)
                await SaveSettingsAsync(_settings, true, token).ConfigureAwait(false);
            return _settings;
        }
    }
}

using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Background;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Settings;
using KaizokuBackend.Services.Providers;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.ComponentModel;
using System.Reflection;

namespace KaizokuBackend.Services.Settings
{
    public class SettingsService
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;
        private readonly IServiceScopeFactory _prov;

        private static SettingsDto? _settings;

        public SettingsService(IConfiguration config, IServiceScopeFactory prov, AppDbContext db)
        {
            _config = config;
            _db = db;
            _prov = prov;

        }


        public async Task<string[]> GetAvailableLanguagesAsync(CancellationToken token = default)
        {
            using (var scope = _prov.CreateScope())
            {
                MihonBridgeService bridgeManager = scope.ServiceProvider.GetRequiredService<MihonBridgeService>();
                var all = bridgeManager.ListOnlineRepositories();
                List<string> languages = all.SelectMany(a=>a.Extensions).SelectMany(a=>a.Sources).Select(a=>a.Language).Distinct()
                    .OrderBy(a => a).ToList();
                languages.Remove("all");
                return languages.ToArray();
            }
        }


        private static List<SettingEntity> Serialize(EditableSettingsDto editableSettings)
        {
            List<SettingEntity> serializedSettings = new List<SettingEntity>();
            List<PropertyInfo> props = typeof(EditableSettingsDto).GetProperties().ToList();
            foreach (PropertyInfo p in props)
            {
                SettingEntity setting = new SettingEntity
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

        private static (bool, EditableSettingsDto) Deserialize(List<SettingEntity> settings, EditableSettingsDto defaultValues) 
        {
            bool needSave = false;
            List<PropertyInfo> props = typeof(EditableSettingsDto).GetProperties().ToList();
            EditableSettingsDto newEditableSettings = new EditableSettingsDto();
            foreach (PropertyInfo p in props)
            {
                string propType = p.PropertyType.Name.ToLowerInvariant();
                SettingEntity? setting = settings.FirstOrDefault(s => s.Name == p.Name);
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

                    setting = new SettingEntity
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
        public void SetThreadSettings(EditableSettingsDto set)
        {
            using (var scope = _prov.CreateScope())
            {
                var settings = scope.ServiceProvider.GetRequiredService<JobsSettings>();
                settings.SetQueueSettings(JobQueues.Downloads, set.NumberOfSimultaneousDownloads, 20, set.NumberOfSimultaneousDownloadsPerProvider, set.ChapterDownloadFailRetryTime);
                settings.SetQueueSettings(JobQueues.Default, 10, set.ChapterDownloadFailRetries, 10, set.ChapterDownloadFailRetryTime);
            }
        }

        public async Task SetTimesSettingsAsync(EditableSettingsDto set, CancellationToken token = default)
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

        public async Task SaveSettingsAsync(EditableSettingsDto set, bool force = false, CancellationToken token = default)
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
            using (var scope = _prov.CreateScope())
            {
                MihonBridgeService bridgeManager = scope.ServiceProvider.GetRequiredService<MihonBridgeService>();
                await bridgeManager.SetPreferencesAsync(new Preferences
                {
                    FlareSolverr = new FlareSolverrPreferences
                    {
                        Enabled = set.FlareSolverrEnabled,
                        Url = set.FlareSolverrUrl,
                        Timeout = (int)set.FlareSolverrTimeout.TotalSeconds,
                        SessionTtl = (int)set.FlareSolverrSessionTtl.TotalSeconds,
                        AsResponseFallback = set.FlareSolverrAsResponseFallback
                    },
                    SocksProxy = new SocksProxyPreferences
                    {
                        Enabled = set.SocksProxyEnabled,
                        Host = set.SocksProxyHost,
                        Port = set.SocksProxyPort,
                        Version = set.SocksProxyVersion,
                        Username = set.SocksProxyUsername,
                        Password = set.SocksProxyPassword
                    }
                }, token).ConfigureAwait(false);
            }
            List<SettingEntity> dbsettings = await _db.Settings.ToListAsync(token).ConfigureAwait(false);
            List<SettingEntity> newSettings = Serialize(set);
            bool needSave = false;
            foreach (SettingEntity setting in newSettings)
            {
                SettingEntity? dbsetting = dbsettings.FirstOrDefault(s => s.Name == setting.Name);
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
        
        public async Task SaveSettingsAsync(SettingsDto settings, bool force, CancellationToken token = default)
        {
            // Convert Settings to EditableSettings since the existing logic works with EditableSettings
            var editableSettings = new EditableSettingsDto
            {
                PreferredLanguages = settings.PreferredLanguages,
                MihonRepositories = settings.MihonRepositories,
                NumberOfSimultaneousDownloads = settings.NumberOfSimultaneousDownloads,
                NumberOfSimultaneousDownloadsPerProvider = settings.NumberOfSimultaneousDownloadsPerProvider,
                NumberOfSimultaneousSearches = settings.NumberOfSimultaneousSearches,
                NumberOfSimultaneousDownloadsPerProvider = settings.NumberOfSimultaneousDownloadsPerProvider,
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
                SocksProxyEnabled = settings.SocksProxyEnabled,
                SocksProxyHost = settings.SocksProxyHost,
                SocksProxyPort = settings.SocksProxyPort,
                SocksProxyVersion = settings.SocksProxyVersion,
                SocksProxyUsername = settings.SocksProxyUsername,
                SocksProxyPassword = settings.SocksProxyPassword
                NsfwVisibility = settings.NsfwVisibility

            };

            await SaveSettingsAsync(editableSettings, force, token).ConfigureAwait(false);
        }
        
        public SettingsDto GetFromEditableSettings(EditableSettingsDto ed)
        {
            SettingsDto set = new SettingsDto
            {
                PreferredLanguages = ed.PreferredLanguages,
                MihonRepositories = ed.MihonRepositories,
                NumberOfSimultaneousDownloads = ed.NumberOfSimultaneousDownloads,
                NumberOfSimultaneousDownloadsPerProvider = ed.NumberOfSimultaneousDownloadsPerProvider,
                NumberOfSimultaneousSearches = ed.NumberOfSimultaneousSearches,
                NumberOfSimultaneousDownloadsPerProvider = ed.NumberOfSimultaneousDownloadsPerProvider,
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
                SocksProxyEnabled = ed.SocksProxyEnabled,
                SocksProxyHost = ed.SocksProxyHost,
                SocksProxyPort = ed.SocksProxyPort,
                SocksProxyVersion = ed.SocksProxyVersion,
                SocksProxyUsername = ed.SocksProxyUsername,
                SocksProxyPassword = ed.SocksProxyPassword
                NsfwVisibility = ed.NsfwVisibility

            };
            set.StorageFolder = _config["StorageFolder"] ?? string.Empty;
            return set;
        }
        public async ValueTask<SettingsDto> GetSettingsAsync(CancellationToken token = default)
        {
            if (_settings != null)
                return _settings;
            SettingsDto firstTimeEditableSettings = new SettingsDto();
            _config.Bind("FirstTimeSettings", firstTimeEditableSettings);
            List<SettingEntity> settings = await _db.Settings.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            bool needSave;
            if (settings.Count == 0)
            {
                _settings = firstTimeEditableSettings;
                needSave = true;
            }
            else
            {
                (needSave, EditableSettingsDto set) = Deserialize(settings, firstTimeEditableSettings);
                _settings = GetFromEditableSettings(set);
            }
            if (needSave)
                await SaveSettingsAsync(_settings, true, token).ConfigureAwait(false);
            return _settings;
        }
    }
}

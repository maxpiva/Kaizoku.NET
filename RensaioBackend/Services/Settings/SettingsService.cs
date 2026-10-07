using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Background;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Jobs.Settings;
using RensaioBackend.Services.Providers;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Settings
{
    public class SettingsService
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;
        private readonly IServiceScopeFactory _prov;

        private static SettingsDto? _settings;

        private readonly ContributionVerificationService _verificationService;

        public SettingsService(
            IConfiguration config,
            IServiceScopeFactory prov,
            AppDbContext db,
            ContributionVerificationService verificationService)
        {
            _config = config;
            _db = db;
            _prov = prov;
            _verificationService = verificationService;
        }


        public SettingsDto? DirectSettings => _settings;

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
                    case "float":
                        setting.Value = ((float)(p.GetValue(editableSettings) ?? 0f)).ToString(CultureInfo.InvariantCulture) ?? "0";
                        break;
                    case "double":
                        setting.Value = ((double)(p.GetValue(editableSettings) ?? 0d)).ToString(CultureInfo.InvariantCulture) ?? "0";
                        break;
                    case "decimal":
                        setting.Value = ((decimal)(p.GetValue(editableSettings) ?? 0m)).ToString(CultureInfo.InvariantCulture) ?? "0";
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
                            // Use InvariantCulture for numeric types to avoid culture-specific decimal separators
                            object? defaultVal = p.GetValue(defaultValues);
                            value = defaultVal switch
                            {
                                double d => d.ToString(CultureInfo.InvariantCulture),
                                float f => f.ToString(CultureInfo.InvariantCulture),
                                decimal m => m.ToString(CultureInfo.InvariantCulture),
                                _ => defaultVal?.ToString() ?? string.Empty
                            };
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
                    case "float":
                        if (float.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float floatValue))
                            p.SetValue(newEditableSettings, floatValue);
                        else {
                            // Parse failed (e.g., corrupted value like "2,0" from culture bug).
                            // Fall back to the default from appsettings.json and mark for save.
                            p.SetValue(newEditableSettings, (float)(p.GetValue(defaultValues) ?? 0f));
                            needSave = true;
                        }
                        break;
                    case "double":
                        if (double.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleValue))
                            p.SetValue(newEditableSettings, doubleValue);
                        else {
                            // Parse failed (e.g., corrupted value like "2,0" from culture bug).
                            // Fall back to the default from appsettings.json and mark for save.
                            p.SetValue(newEditableSettings, (double)(p.GetValue(defaultValues) ?? 0d));
                            needSave = true;
                        }
                        break;
                    case "decimal":
                        if (decimal.TryParse(setting.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decimalValue))
                            p.SetValue(newEditableSettings, decimalValue);
                        else {
                            // Parse failed (e.g., corrupted value like "2,0" from culture bug).
                            // Fall back to the default from appsettings.json and mark for save.
                            p.SetValue(newEditableSettings, (decimal)(p.GetValue(defaultValues) ?? 0m));
                            needSave = true;
                        }
                        break;
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

        public async Task SaveSettingsAsync(EditableSettingsDto set, bool force = false, CancellationToken token = default, bool clearOidcClientSecret = false)
        {
            var filenameError = RensaioBackend.Services.Helpers.ChapterFilenameTemplate.Validate(set.ChapterFilenameTemplate);
            if (filenameError != null) throw new ArgumentException(filenameError);
            set.ChapterFilenameTemplate = set.ChapterFilenameTemplate?.Trim() ?? "";
            await PreserveStoredOidcValuesAsync(set, clearOidcClientSecret, token).ConfigureAwait(false);
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
                var onlineRepos = bridgeManager.ListOnlineRepositories();
                List<string> repos = set.MihonRepositories.ToList();
                foreach (var t in onlineRepos)
                {
                    foreach (string s in repos.ToList())
                    {
                        if (s.Equals(t.Url, StringComparison.OrdinalIgnoreCase))
                        {
                            repos.Remove(s);
                            break;
                        }
                    }
                }
                if (repos.Count>0)
                {
                    foreach(string n in repos)
                    {
                        TachiyomiRepository repo = new TachiyomiRepository(n);
                        repo = await bridgeManager.AddOnlineRepositoryAsync(repo).ConfigureAwait(false);
                        if (!n.Equals(repo.Url, StringComparison.OrdinalIgnoreCase))
                        {
                            List<string> existing = set.MihonRepositories.ToList();
                            existing.Remove(n);
                            existing.Add(repo.Url);
                            set.MihonRepositories = existing.ToArray();
                        }
                    }
                }
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
                    },
                    Cef = new CefPreferences
                    {
                        MaxRenderers = set.CefMaxRenderers,
                        IdleTimeoutMs = set.CefIdleTimeoutMs,
                        WebViewPoolEnabled = set.CefWebViewPoolEnabled,
                        Enabled = set.CefEnabled,
                        PumpActiveIntervalMs = set.CefPumpActiveIntervalMs,
                        PumpIdleIntervalMs = set.CefPumpIdleIntervalMs
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
            }            
            if (needSave)
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
                ChapterDownloadFailRetryTime = settings.ChapterDownloadFailRetryTime,
                ChapterDownloadFailRetries = settings.ChapterDownloadFailRetries,
                PerTitleUpdateSchedule = settings.PerTitleUpdateSchedule,
                PerSourceUpdateSchedule = settings.PerSourceUpdateSchedule,
                ExtensionsCheckForUpdateSchedule = settings.ExtensionsCheckForUpdateSchedule,
                CategorizedFolders = settings.CategorizedFolders,
                ChapterFilenameTemplate = settings.ChapterFilenameTemplate,
                InjectSeriesCover = settings.InjectSeriesCover,
                Categories = settings.Categories,
                FlareSolverrEnabled = settings.FlareSolverrEnabled,
                FlareSolverrUrl = settings.FlareSolverrUrl,
                FlareSolverrTimeout = settings.FlareSolverrTimeout,
                FlareSolverrSessionTtl = settings.FlareSolverrSessionTtl,
                FlareSolverrAsResponseFallback = settings.FlareSolverrAsResponseFallback,
                CefMaxRenderers = settings.CefMaxRenderers,
                CefIdleTimeoutMs = settings.CefIdleTimeoutMs,
                CefWebViewPoolEnabled = settings.CefWebViewPoolEnabled,
                CefEnabled = settings.CefEnabled,
                CefPumpActiveIntervalMs = settings.CefPumpActiveIntervalMs,
                CefPumpIdleIntervalMs = settings.CefPumpIdleIntervalMs,
                IsWizardSetupComplete = settings.IsWizardSetupComplete,
                WizardSetupStepCompleted = settings.WizardSetupStepCompleted,
                SocksProxyEnabled = settings.SocksProxyEnabled,
                SocksProxyHost = settings.SocksProxyHost,
                SocksProxyPort = settings.SocksProxyPort,
                SocksProxyVersion = settings.SocksProxyVersion,
                SocksProxyUsername = settings.SocksProxyUsername,
                SocksProxyPassword = settings.SocksProxyPassword,
                NsfwVisibility = settings.NsfwVisibility,
                ReleaseCadenceMultiplierYellow = settings.ReleaseCadenceMultiplierYellow,
                ReleaseCadenceMultiplierRed = settings.ReleaseCadenceMultiplierRed,
                ReleaseCadenceDefaultDays = settings.ReleaseCadenceDefaultDays,
                ProviderErrorYellowHours = settings.ProviderErrorYellowHours,
                ProviderErrorRedHours = settings.ProviderErrorRedHours,
                AuthenticationEnabled = settings.AuthenticationEnabled,
                ExternalDomain = settings.ExternalDomain,
                OidcEnabled = settings.OidcEnabled,
                OidcIssuer = settings.OidcIssuer,
                OidcClientId = settings.OidcClientId,
                OidcClientSecret = settings.OidcClientSecret,
                OidcButtonLabel = settings.OidcButtonLabel,
                ContributionEnabled = settings.ContributionEnabled,
                ContributionServerUrl = settings.ContributionServerUrl,
                ContributionContributorId = settings.ContributionContributorId,
                ContributionExportRepo = settings.ContributionExportRepo,
                // Server-computed — preserve the currently stored verified state. The
                // client payload can never flip this flag; only an actual verification
                // round-trip against the contribution worker may set it.
                ContributionVerified = _settings?.ContributionVerified ?? false,
            };

            await SaveSettingsAsync(editableSettings, force, token, clearOidcClientSecret: settings.OidcClearClientSecret).ConfigureAwait(false);
        }

        /// <summary>
        /// Protects the stored OIDC values on every save path (REST and MCP):
        /// - when config/env supplies them, the caller only ever saw effective values,
        ///   so keep what is stored and never write an environment secret to the DB;
        /// - otherwise an empty client secret means "keep the current one", because
        ///   GET never returns the secret to clients; <paramref name="clearClientSecret"/>
        ///   is the explicit way to remove it (switch to a public client).
        /// </summary>
        private async Task PreserveStoredOidcValuesAsync(EditableSettingsDto set, bool clearClientSecret, CancellationToken token)
        {
            string[] names =
            [
                nameof(EditableSettingsDto.OidcEnabled),
                nameof(EditableSettingsDto.OidcIssuer),
                nameof(EditableSettingsDto.OidcClientId),
                nameof(EditableSettingsDto.OidcClientSecret),
                nameof(EditableSettingsDto.OidcButtonLabel),
            ];
            var stored = await _db.Settings.AsNoTracking()
                .Where(s => names.Contains(s.Name))
                .ToDictionaryAsync(s => s.Name, s => s.Value, token).ConfigureAwait(false);

            if (Auth.Oidc.OidcOptions.IsManagedByConfig(_config))
            {
                set.OidcEnabled = stored.TryGetValue(nameof(EditableSettingsDto.OidcEnabled), out var e) && bool.TryParse(e, out var eb) && eb;
                set.OidcIssuer = stored.GetValueOrDefault(nameof(EditableSettingsDto.OidcIssuer)) ?? string.Empty;
                set.OidcClientId = stored.GetValueOrDefault(nameof(EditableSettingsDto.OidcClientId)) ?? string.Empty;
                set.OidcClientSecret = stored.GetValueOrDefault(nameof(EditableSettingsDto.OidcClientSecret)) ?? string.Empty;
                set.OidcButtonLabel = stored.GetValueOrDefault(nameof(EditableSettingsDto.OidcButtonLabel)) ?? "Single Sign-On";
            }
            else if (clearClientSecret)
            {
                set.OidcClientSecret = string.Empty;
            }
            else if (string.IsNullOrEmpty(set.OidcClientSecret))
            {
                set.OidcClientSecret = stored.GetValueOrDefault(nameof(EditableSettingsDto.OidcClientSecret)) ?? string.Empty;
            }
        }

        /// <summary>
        /// Verifies a Contributor Id against the configured contribution server
        /// (RensaioContributionDB.CF). Pure validation — does not persist anything.
        /// </summary>
        public async Task<ContributionVerificationResult> VerifyContributorAsync(
            string serverUrl,
            string contributorId,
            CancellationToken token = default)
        {
            return await _verificationService.VerifyAsync(serverUrl, contributorId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists the verified flag (and, when provided, the contributor id and
        /// server URL so a post-verify settings refetch keeps them) and refreshes
        /// the in-memory settings cache so downstream gates pick it up immediately.
        /// </summary>
        public async Task SetContributionVerifiedAsync(
            bool verified,
            CancellationToken token = default,
            string? contributorId = null,
            string? serverUrl = null)
        {
            await UpsertSettingAsync(nameof(EditableSettingsDto.ContributionVerified), verified.ToString(), token).ConfigureAwait(false);
            if (contributorId != null)
            {
                await UpsertSettingAsync(nameof(EditableSettingsDto.ContributionContributorId), contributorId, token).ConfigureAwait(false);
            }
            if (serverUrl != null)
            {
                await UpsertSettingAsync(nameof(EditableSettingsDto.ContributionServerUrl), serverUrl, token).ConfigureAwait(false);
            }

            if (_settings != null)
            {
                _settings.ContributionVerified = verified;
                if (contributorId != null)
                {
                    _settings.ContributionContributorId = contributorId;
                }
                if (serverUrl != null)
                {
                    _settings.ContributionServerUrl = serverUrl;
                }
            }
        }

        private async Task UpsertSettingAsync(string name, string value, CancellationToken token)
        {
            SettingEntity? setting = await _db.Settings
                .FirstOrDefaultAsync(s => s.Name == name, token)
                .ConfigureAwait(false);

            if (setting == null)
            {
                _db.Settings.Add(new SettingEntity { Name = name, Value = value });
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
            else if (setting.Value != value)
            {
                setting.Value = value;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>Reads a single key-value setting, or null when absent.</summary>
        public async Task<string?> GetSettingValueAsync(string name, CancellationToken token = default)
        {
            return await _db.Settings.AsNoTracking()
                .Where(s => s.Name == name)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>Writes a single key-value setting (upsert; no schema change).</summary>
        public async Task SetSettingValueAsync(string name, string value, CancellationToken token = default)
        {
            await UpsertSettingAsync(name, value, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Settings as they may be shown to clients: a copy of the effective settings with
        /// the OIDC client secret blanked. Every read path that leaves the process
        /// (REST GET, MCP get_settings) goes through here.
        /// </summary>
        public async ValueTask<SettingsDto> GetSettingsForClientAsync(CancellationToken token = default)
        {
            var settings = await GetSettingsAsync(token).ConfigureAwait(false);
            var view = GetFromEditableSettings(settings);
            view.OidcClientSecretSet = !string.IsNullOrEmpty(view.OidcClientSecret);
            view.OidcClientSecret = string.Empty;
            return view;
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
                ChapterDownloadFailRetryTime = ed.ChapterDownloadFailRetryTime,
                ChapterDownloadFailRetries = ed.ChapterDownloadFailRetries,
                PerTitleUpdateSchedule = ed.PerTitleUpdateSchedule,
                PerSourceUpdateSchedule = ed.PerSourceUpdateSchedule,
                ExtensionsCheckForUpdateSchedule = ed.ExtensionsCheckForUpdateSchedule,
                CategorizedFolders = ed.CategorizedFolders,
                ChapterFilenameTemplate = ed.ChapterFilenameTemplate,
                InjectSeriesCover = ed.InjectSeriesCover,
                Categories = ed.Categories,
                FlareSolverrEnabled = ed.FlareSolverrEnabled,
                FlareSolverrUrl = ed.FlareSolverrUrl,
                FlareSolverrTimeout = ed.FlareSolverrTimeout,
                FlareSolverrSessionTtl = ed.FlareSolverrSessionTtl,
                FlareSolverrAsResponseFallback = ed.FlareSolverrAsResponseFallback,
                CefMaxRenderers = ed.CefMaxRenderers,
                CefIdleTimeoutMs = ed.CefIdleTimeoutMs,
                CefWebViewPoolEnabled = ed.CefWebViewPoolEnabled,
                CefEnabled = ed.CefEnabled,
                CefPumpActiveIntervalMs = ed.CefPumpActiveIntervalMs,
                CefPumpIdleIntervalMs = ed.CefPumpIdleIntervalMs,
                IsWizardSetupComplete = ed.IsWizardSetupComplete,
                WizardSetupStepCompleted = ed.WizardSetupStepCompleted,
                SocksProxyEnabled = ed.SocksProxyEnabled,
                SocksProxyHost = ed.SocksProxyHost,
                SocksProxyPort = ed.SocksProxyPort,
                SocksProxyVersion = ed.SocksProxyVersion,
                SocksProxyUsername = ed.SocksProxyUsername,
                SocksProxyPassword = ed.SocksProxyPassword,
                NsfwVisibility = ed.NsfwVisibility,
                ReleaseCadenceMultiplierYellow = ed.ReleaseCadenceMultiplierYellow,
                ReleaseCadenceMultiplierRed = ed.ReleaseCadenceMultiplierRed,
                ReleaseCadenceDefaultDays = ed.ReleaseCadenceDefaultDays,
                ProviderErrorYellowHours = ed.ProviderErrorYellowHours,
                ProviderErrorRedHours = ed.ProviderErrorRedHours,
                AuthenticationEnabled = ed.AuthenticationEnabled,
                ExternalDomain = ed.ExternalDomain,
                OidcEnabled = ed.OidcEnabled,
                OidcIssuer = ed.OidcIssuer,
                OidcClientId = ed.OidcClientId,
                OidcClientSecret = ed.OidcClientSecret,
                OidcButtonLabel = ed.OidcButtonLabel,
                ContributionEnabled = ed.ContributionEnabled,
                ContributionServerUrl = ed.ContributionServerUrl,
                ContributionContributorId = ed.ContributionContributorId,
                ContributionExportRepo = ed.ContributionExportRepo,
                ContributionVerified = ed.ContributionVerified,
            };
            set.StorageFolder = _config["StorageFolder"] ?? string.Empty;
            set.OidcManagedByConfig = Auth.Oidc.OidcOptions.IsManagedByConfig(_config);
            if (set.OidcManagedByConfig)
            {
                // Show the effective values so the (read-only) Settings fields reflect
                // reality. SaveSettingsAsync(SettingsDto) keeps them out of the database.
                var effective = Auth.Oidc.OidcOptions.Resolve(_config, ed);
                set.OidcEnabled = effective.Enabled;
                set.OidcIssuer = effective.Issuer;
                set.OidcClientId = effective.ClientId;
                set.OidcClientSecret = effective.ClientSecret;
                set.OidcButtonLabel = effective.ButtonLabel;
                set.OidcConfigError = effective.BindError;
            }
            return set;
        }
        /// <summary>
        /// Validates and self-heals ReleaseCadenceMultiplier values.
        /// If either multiplier is 0 (corrupted from culture-misparse), replaces it
        /// with the default from appsettings.json (or code-level fallback of 2.0/5.0).
        /// Returns true if any value was changed (caller should save).
        /// </summary>
        private static bool ValidateCadenceMultipliers(EditableSettingsDto settings, SettingsDto defaults)
        {
            bool changed = false;
            double defaultYellow = defaults.ReleaseCadenceMultiplierYellow;
            double defaultRed = defaults.ReleaseCadenceMultiplierRed;

            if (settings.ReleaseCadenceMultiplierYellow <= 0d)
            {
                settings.ReleaseCadenceMultiplierYellow = defaultYellow > 0d ? defaultYellow : 2.0;
                changed = true;
            }
            if (settings.ReleaseCadenceMultiplierRed <= 0d)
            {
                settings.ReleaseCadenceMultiplierRed = defaultRed > 0d ? defaultRed : 5.0;
                changed = true;
            }
            return changed;
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

                // Validate cadence multipliers: if they're 0 (corrupted from culture-misparse),
                // restore from defaults and mark for re-save to heal the DB.
                if (ValidateCadenceMultipliers(set, firstTimeEditableSettings))
                {
                    needSave = true;
                }

                _settings = GetFromEditableSettings(set);
            }
            if (needSave)
                await SaveSettingsAsync(_settings, true, token).ConfigureAwait(false);
            return _settings;
        }
    }
}

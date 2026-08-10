using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Background;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Jobs.Settings;
using RensaioBackend.Services.Providers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RensaioBackend.Services.Settings
{
    public class SettingsService
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;
        private readonly IServiceScopeFactory _prov;
        private readonly ILogger<SettingsService> _logger;

        private static SettingsDto? _settings;

        public SettingsService(IConfiguration config, IServiceScopeFactory prov, AppDbContext db,
            ILogger<SettingsService>? logger = null)
        {
            _config = config;
            _db = db;
            _prov = prov;
            _logger = logger ?? NullLogger<SettingsService>.Instance;

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
                        string[] array = p.GetValue(editableSettings) as string[] ?? [];
                        // Persist as JSON: entries such as ContributionSourceAllowlist's
                        // "package|numericSourceId" contain '|' and would be corrupted by a
                        // '|'-joined round-trip. Reads fall back to the legacy '|' format.
                        setting.Value = JsonSerializer.Serialize(array);
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
                            value = JsonSerializer.Serialize(split);
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
                        p.SetValue(newEditableSettings, ParseStringArray(setting.Value));
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
        /// <summary>
        /// Parses a persisted string-array setting. New values are stored as JSON arrays;
        /// values from existing databases use the legacy '|'-joined format, so anything that
        /// does not parse as a JSON array falls back to a '|' split.
        /// </summary>
        private static string[] ParseStringArray(string value)
        {
            if (value.TrimStart().StartsWith('['))
            {
                try
                {
                    return JsonSerializer.Deserialize<string[]>(value) ?? [];
                }
                catch (JsonException)
                {
                    // Not actually JSON (e.g. a legacy value that happens to start with '[');
                    // fall through to the legacy format.
                }
            }
            return value.Split('|');
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

        /// <summary>
        /// Placeholder the settings API returns instead of the stored contributor UUID
        /// (GET /api/settings is reachable without auth, and the UUID is the upload
        /// credential). A PUT that round-trips the sentinel keeps the stored value.
        /// </summary>
        public const string UuidSentinel = "__SET__";

        /// <summary>
        /// Resolves the incoming contributor UUID against the sentinel contract: the sentinel
        /// keeps the currently stored value, a parseable UUID (or empty, to clear) replaces it,
        /// and anything else is rejected.
        /// </summary>
        public static void ApplyContributorUuidPolicy(EditableSettingsDto set, EditableSettingsDto? current)
        {
            if (set.ContributionContributorUuid == UuidSentinel)
                set.ContributionContributorUuid = current?.ContributionContributorUuid ?? string.Empty;
            else if (!string.IsNullOrWhiteSpace(set.ContributionContributorUuid) &&
                     !Guid.TryParse(set.ContributionContributorUuid, out _))
                throw new ArgumentException("contributionContributorUuid must be a UUID.", nameof(set));
        }

        /// <summary>
        /// Endpoint path segments <see cref="Contributions.Upload.ContributionUploadClient"/> appends
        /// to the configured base URL. A pasted full endpoint link (e.g.
        /// "https://contribution.rensaio.net/contributor?contributor=&lt;uuid&gt;", the shape of the
        /// example link shown in the UI) ends in one of these, which is how normalization tells it
        /// apart from a legitimate base-URL subpath (a self-hosted worker can live under its own
        /// subpath, e.g. "https://host/worker").
        /// </summary>
        private static readonly string[] UploadWorkerEndpointSegments = ["contributor", "upload"];

        /// <summary>
        /// Normalizes and validates a contribution worker base URL entered as a raw setting value:
        /// trims whitespace, prepends "https://" when the value has no scheme, strips a query
        /// string/fragment and (for <paramref name="stripKnownEndpointSegment"/> callers) a trailing
        /// endpoint path segment that indicate the value is a pasted full worker link rather than a
        /// base URL, and throws <see cref="ArgumentException"/> — the same rejection mechanism as
        /// <see cref="ApplyContributorUuidPolicy"/>'s malformed-UUID case — when what remains still
        /// isn't an absolute http(s) URL. A legitimate base path (e.g. a self-hosted worker under a
        /// subpath, or the default snapshot export's "/maxpiva/Rensaio-Metadata/main") is preserved.
        /// Anything normalized away is appended to <paramref name="warnings"/> for the caller to log;
        /// a stray "contributor=&lt;uuid&gt;" query pair is never adopted into the stored contributor
        /// UUID — it is only ever stripped and logged, since a URL field must never become a source of
        /// truth for a credential.
        /// </summary>
        private static string NormalizeContributionUrl(string fieldName, string? rawValue,
            bool stripKnownEndpointSegment, string? contributorUuid, ICollection<string> warnings)
        {
            string trimmed = (rawValue ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return trimmed;

            string candidate = trimmed;
            if (!candidate.Contains("://", StringComparison.Ordinal))
            {
                candidate = "https://" + candidate;
                warnings.Add($"{fieldName} had no scheme; assumed https://.");
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host))
                throw new ArgumentException($"{fieldName} must be a valid absolute http(s) URL.", fieldName);

            string path = uri.AbsolutePath;
            if (stripKnownEndpointSegment)
            {
                string trimmedPath = path.TrimEnd('/');
                string? matched = UploadWorkerEndpointSegments.FirstOrDefault(seg =>
                    trimmedPath.EndsWith("/" + seg, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    path = trimmedPath[..^(matched.Length + 1)];
                    warnings.Add($"{fieldName} looked like a pasted worker endpoint link (\"/{matched}\"); trimmed to its base URL.");
                }
            }

            if (uri.Query.Length > 0)
            {
                var query = QueryHelpers.ParseQuery(uri.Query);
                bool hadContributorParam = query.TryGetValue("contributor", out var contributorValues) &&
                    contributorValues.Any(v => Guid.TryParse(v, out _));
                if (hadContributorParam)
                {
                    warnings.Add($"{fieldName} contained a 'contributor' query parameter; it was removed and NOT " +
                        (string.IsNullOrWhiteSpace(contributorUuid)
                            ? "applied to the stored contributor UUID (which is currently empty)."
                            : "applied to the stored contributor UUID."));
                }
                else
                {
                    warnings.Add($"{fieldName} contained a query string that was removed.");
                }
            }
            if (uri.Fragment.Length > 0)
                warnings.Add($"{fieldName} contained a fragment that was removed.");

            // A bare root path ("/") is not a meaningful base path; drop it so a scheme-only
            // input round-trips as "https://host" rather than "https://host/".
            if (path == "/")
                path = string.Empty;

            return $"{uri.Scheme}://{uri.Authority}{path}";
        }

        /// <summary>
        /// Normalizes and validates <see cref="EditableSettingsDto.ContributionUploadUrl"/> and
        /// <see cref="EditableSettingsDto.ContributionSnapshotUrl"/> in place. Returns any
        /// human-readable warnings describing what was normalized away, for the caller to log.
        /// </summary>
        public static List<string> ApplyContributionUrlPolicy(EditableSettingsDto set)
        {
            var warnings = new List<string>();
            set.ContributionUploadUrl = NormalizeContributionUrl(
                "contributionUploadUrl", set.ContributionUploadUrl, stripKnownEndpointSegment: true,
                set.ContributionContributorUuid, warnings);
            set.ContributionSnapshotUrl = NormalizeContributionUrl(
                "contributionSnapshotUrl", set.ContributionSnapshotUrl, stripKnownEndpointSegment: false,
                set.ContributionContributorUuid, warnings);
            return warnings;
        }

        public async Task SaveSettingsAsync(EditableSettingsDto set, bool force = false, CancellationToken token = default)
        {
            ApplyContributorUuidPolicy(set, _settings);
            foreach (string warning in ApplyContributionUrlPolicy(set))
                _logger.LogWarning("{Warning}", warning);
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
            // The discovery-eligible extension set depends on preferred languages and NSFW
            // visibility; re-run the artifact precache job when they (or its own toggles) change.
            if (_settings != null &&
                (JoinAndSortArray(set.PreferredLanguages) != JoinAndSortArray(_settings.PreferredLanguages) ||
                 set.NsfwVisibility != _settings.NsfwVisibility ||
                 set.DiscoveryPrecacheEnabled != _settings.DiscoveryPrecacheEnabled ||
                 set.DiscoveryIncludeInSearch != _settings.DiscoveryIncludeInSearch ||
                 set.MaxDiscoverySearchExtensions != _settings.MaxDiscoverySearchExtensions))
            {
                using var jobScope = _prov.CreateScope();
                var jobBusiness = jobScope.ServiceProvider.GetRequiredService<JobBusinessService>();
                await jobBusiness.ManageDiscoveryPrecacheAsync(
                    set.DiscoveryIncludeInSearch && set.DiscoveryPrecacheEnabled, runNow: true, token).ConfigureAwait(false);
            }
            bool contributionSettingsChanged = _settings != null &&
                (set.ContributionCollectorEnabled != _settings.ContributionCollectorEnabled ||
                 JoinAndSortArray(set.ContributionPackageAllowlist) != JoinAndSortArray(_settings.ContributionPackageAllowlist) ||
                 JoinAndSortArray(set.ContributionSourceAllowlist) != JoinAndSortArray(_settings.ContributionSourceAllowlist));
            bool contributionUploadSettingsChanged = _settings != null &&
                (set.ContributionUploadEnabled != _settings.ContributionUploadEnabled ||
                 !string.Equals(set.ContributionContributorUuid, _settings.ContributionContributorUuid, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(set.ContributionUploadUrl, _settings.ContributionUploadUrl, StringComparison.OrdinalIgnoreCase));
            bool contributionSnapshotSettingsChanged = _settings != null &&
                (set.ContributionSnapshotEnabled != _settings.ContributionSnapshotEnabled ||
                 !string.Equals(set.ContributionSnapshotUrl, _settings.ContributionSnapshotUrl, StringComparison.OrdinalIgnoreCase));
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
            if (contributionSettingsChanged)
            {
                using var jobScope = _prov.CreateScope();
                var jobBusiness = jobScope.ServiceProvider.GetRequiredService<JobBusinessService>();
                await jobBusiness.ManageContributionCollectorAsync(
                    set.ContributionCollectorEnabled, runNow: set.ContributionCollectorEnabled, token).ConfigureAwait(false);
            }
            if (contributionUploadSettingsChanged)
            {
                using var jobScope = _prov.CreateScope();
                var jobBusiness = jobScope.ServiceProvider.GetRequiredService<JobBusinessService>();
                await jobBusiness.ManageContributionUploaderAsync(
                    set.ContributionUploadEnabled, runNow: set.ContributionUploadEnabled, token).ConfigureAwait(false);
            }
            if (contributionSnapshotSettingsChanged)
            {
                using var jobScope = _prov.CreateScope();
                var jobBusiness = jobScope.ServiceProvider.GetRequiredService<JobBusinessService>();
                await jobBusiness.ManageContributionSnapshotAsync(
                    set.ContributionSnapshotEnabled, runNow: set.ContributionSnapshotEnabled, token).ConfigureAwait(false);
            }
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
                MaxDiscoverySearchExtensions = settings.MaxDiscoverySearchExtensions,
                DiscoverySearchWorkersEnabled = settings.DiscoverySearchWorkersEnabled,
                DiscoveryWorkerBatchSize = settings.DiscoveryWorkerBatchSize,
                MaxDiscoveryWorkers = settings.MaxDiscoveryWorkers,
                DiscoveryIncludeInSearch = settings.DiscoveryIncludeInSearch,
                DiscoveryPrecacheEnabled = settings.DiscoveryPrecacheEnabled,
                DiscoveryWarmPoolEnabled = settings.DiscoveryWarmPoolEnabled,
                DiscoveryWorkerIdleTimeout = settings.DiscoveryWorkerIdleTimeout,
                ContributionCollectorEnabled = settings.ContributionCollectorEnabled,
                ContributionPackageAllowlist = settings.ContributionPackageAllowlist,
                ContributionSourceAllowlist = settings.ContributionSourceAllowlist,
                ContributionUploadEnabled = settings.ContributionUploadEnabled,
                ContributionContributorUuid = settings.ContributionContributorUuid,
                ContributionUploadUrl = settings.ContributionUploadUrl,
                ContributionSnapshotEnabled = settings.ContributionSnapshotEnabled,
                ContributionSnapshotUrl = settings.ContributionSnapshotUrl,
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
                SocksProxyPassword = settings.SocksProxyPassword,
                NsfwVisibility = settings.NsfwVisibility,
                ReleaseCadenceMultiplierYellow = settings.ReleaseCadenceMultiplierYellow,
                ReleaseCadenceMultiplierRed = settings.ReleaseCadenceMultiplierRed,
                ReleaseCadenceDefaultDays = settings.ReleaseCadenceDefaultDays,
                ProviderErrorYellowHours = settings.ProviderErrorYellowHours,
                ProviderErrorRedHours = settings.ProviderErrorRedHours,
                AuthenticationEnabled = settings.AuthenticationEnabled,
                ExternalDomain = settings.ExternalDomain,
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
                MaxDiscoverySearchExtensions = ed.MaxDiscoverySearchExtensions,
                DiscoverySearchWorkersEnabled = ed.DiscoverySearchWorkersEnabled,
                DiscoveryWorkerBatchSize = ed.DiscoveryWorkerBatchSize,
                MaxDiscoveryWorkers = ed.MaxDiscoveryWorkers,
                DiscoveryIncludeInSearch = ed.DiscoveryIncludeInSearch,
                DiscoveryPrecacheEnabled = ed.DiscoveryPrecacheEnabled,
                DiscoveryWarmPoolEnabled = ed.DiscoveryWarmPoolEnabled,
                DiscoveryWorkerIdleTimeout = ed.DiscoveryWorkerIdleTimeout,
                ContributionCollectorEnabled = ed.ContributionCollectorEnabled,
                ContributionPackageAllowlist = ed.ContributionPackageAllowlist,
                ContributionSourceAllowlist = ed.ContributionSourceAllowlist,
                ContributionUploadEnabled = ed.ContributionUploadEnabled,
                ContributionContributorUuid = ed.ContributionContributorUuid,
                ContributionUploadUrl = ed.ContributionUploadUrl,
                ContributionSnapshotEnabled = ed.ContributionSnapshotEnabled,
                ContributionSnapshotUrl = ed.ContributionSnapshotUrl,
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
                SocksProxyPassword = ed.SocksProxyPassword,
                NsfwVisibility = ed.NsfwVisibility,
                ReleaseCadenceMultiplierYellow = ed.ReleaseCadenceMultiplierYellow,
                ReleaseCadenceMultiplierRed = ed.ReleaseCadenceMultiplierRed,
                ReleaseCadenceDefaultDays = ed.ReleaseCadenceDefaultDays,
                ProviderErrorYellowHours = ed.ProviderErrorYellowHours,
                ProviderErrorRedHours = ed.ProviderErrorRedHours,
                AuthenticationEnabled = ed.AuthenticationEnabled,
                ExternalDomain = ed.ExternalDomain,
            };
            set.StorageFolder = _config["StorageFolder"] ?? string.Empty;
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

        /// <summary>
        /// Settings as served by the API: a copy with the contributor UUID replaced by
        /// <see cref="UuidSentinel"/> when one is stored, so the credential never leaves
        /// the backend.
        /// </summary>
        public async ValueTask<SettingsDto> GetMaskedSettingsAsync(CancellationToken token = default)
        {
            SettingsDto settings = await GetSettingsAsync(token).ConfigureAwait(false);
            SettingsDto masked = GetFromEditableSettings(settings);
            if (!string.IsNullOrEmpty(masked.ContributionContributorUuid))
                masked.ContributionContributorUuid = UuidSentinel;
            return masked;
        }
    }
}

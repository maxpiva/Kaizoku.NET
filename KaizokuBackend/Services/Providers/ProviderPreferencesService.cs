using KaizokuBackend.Data;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Text.Json;
using ValueType = KaizokuBackend.Models.ValueType;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider preferences management following SRP
    /// </summary>
    public class ProviderPreferencesService
    {
        private readonly SuwayomiClient _suwayomiClient;
        private readonly ProviderCacheService _providerCache;
        private readonly AppDbContext _db;
        private readonly ILogger<ProviderPreferencesService> _logger;

        public ProviderPreferencesService(SuwayomiClient suwayomiClient, ProviderCacheService providerCache, AppDbContext db, ILogger<ProviderPreferencesService> logger)
        {
            _suwayomiClient = suwayomiClient;
            _providerCache = providerCache;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Gets provider preferences by APK name
        /// </summary>
        /// <param name="apkName">APK name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Provider preferences or null if not found</returns>
        public async Task<ProviderPreferences?> GetProviderPreferencesAsync(string apkName, CancellationToken token = default)
        {
            try
            {
                var providers = await _providerCache.GetCachedProvidersAsync(token).ConfigureAwait(false);
                var provider = providers.FirstOrDefault(a => a.ApkName == apkName);
                if (provider == null)
                    return null;

                // Create storage preference
                var storagePreference = CreateStoragePreference(provider);
                var preferences = new List<SuwayomiPreference> { storagePreference };

                // Get all unique preferences ordered by English first
                var allPreferences = OrderByEnglishFirst(provider.Mappings.ToList())
                    .SelectMany(a => a.Preferences)
                    .DistinctBy(a => a.props.key)
                    .ToList();

                // Fetch current preferences from Suwayomi
                var sourceDict = new ConcurrentDictionary<string, List<SuwayomiPreference>>();
                var sourceNames = allPreferences.Select(a => a.Source).Distinct().ToList();
                
                await Parallel.ForEachAsync(sourceNames, 
                    new ParallelOptions { MaxDegreeOfParallelism = 10 },
                    async (sourceName, _) =>
                    {
                        var source = provider.Mappings.First(a => a.Source?.Id == sourceName).Source;
                        if (source != null)
                        {
                            var prefs = await _suwayomiClient.GetSourcePreferencesAsync(source.Id, token).ConfigureAwait(false);
                            RemoveSuffixPreferences(provider.Lang, source.Id, prefs);
                            sourceDict[source.Id] = prefs;
                        }
                    }).ConfigureAwait(false);

                // Build updated preferences list
                var updatedPreferences = new List<SuwayomiPreference>();
                foreach (var preference in allPreferences)
                {
                    var updatedPref = sourceDict[preference.Source].First(a => a.props.key == preference.props.key);
                    updatedPreferences.Add(updatedPref);
                }

                preferences.AddRange(updatedPreferences);
                return ConvertToProviderPreferences(apkName, preferences);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting provider preferences for {ApkName}", apkName);
                return null;
            }
        }

        /// <summary>
        /// Sets provider preferences
        /// </summary>
        /// <param name="preferences">Provider preferences to set</param>
        /// <param name="token">Cancellation token</param>
        public async Task SetProviderPreferencesAsync(ProviderPreferences preferences, CancellationToken token = default)
        {
            try
            {
                var providers = await _providerCache.GetCachedProvidersAsync(token).ConfigureAwait(false);
                var provider = providers.FirstOrDefault(a => a.ApkName == preferences.ApkName);
                if (provider == null)
                    return;

                // Handle storage preference
                var storagePreference = preferences.Preferences.FirstOrDefault(a => a.Key == "isStorage");
                if (storagePreference != null)
                {
                    var storageValue = (string)ConvertJsonObject(storagePreference.CurrentValue!);
                    provider.IsStorage = storageValue == "permanent";
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                    preferences.Preferences.Remove(storagePreference);
                }

                // Handle source preferences
                if (preferences.Preferences.Count > 0)
                {
                    await UpdateSourcePreferencesAsync(provider, preferences.Preferences, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting provider preferences for {ApkName}", preferences.ApkName);
                throw;
            }
        }

        #region Private Helper Methods

        private static SuwayomiPreference CreateStoragePreference(ProviderStorage provider)
        {
            return new SuwayomiPreference
            {
                type = "ListPreference",
                props = new SuwayomiProp
                {
                    key = "isStorage",
                    title = "Provider Download Defaults",
                    summary = "Permanent providers always download new chapters and replace any existing copies from temporary providers.\nTemporary providers only download a chapter if they are the first to have it available.",
                    entries = new List<string> { "Permanent", "Temporary" },
                    entryValues = new List<string> { "permanent", "temporary" },
                    defaultValueType = "String",
                    defaultValue = "permanent",
                    currentValue = provider.IsStorage ? "permanent" : "temporary"
                }
            };
        }

        private static List<Mappings> OrderByEnglishFirst(List<Mappings> mappings)
        {
            var result = new List<Mappings>();
            var englishMapping = mappings.FirstOrDefault(a => a.Source != null && a.Source.Lang == "en");
            if (englishMapping != null)
            {
                result.Add(englishMapping);
                mappings.Remove(englishMapping);
            }
            result.AddRange(mappings.OrderBy(a => a.Source?.Lang ?? ""));
            return result;
        }

        private static void RemoveSuffixPreferences(string extensionLang, string sourceId, List<SuwayomiPreference> preferences)
        {
            preferences.ForEach(pref =>
            {
                if (extensionLang == "all")
                {
                    int lastUnderscore = pref.props.key.LastIndexOf('_');
                    if (lastUnderscore > 0)
                    {
                        pref.props.key = pref.props.key.Substring(0, lastUnderscore);
                    }
                }
                pref.Source = sourceId;
            });
        }

        private async Task UpdateSourcePreferencesAsync(ProviderStorage provider, List<ProviderPreference> preferences, CancellationToken token)
        {
            var sourceNames = preferences.Select(a => a.Source).Distinct().ToList();
            var sourceDict = new ConcurrentDictionary<string, List<SuwayomiPreference>>();
            
            await Parallel.ForEachAsync(sourceNames, new ParallelOptions { MaxDegreeOfParallelism = 10 },
                async (sourceName, _) =>
                {
                    var source = provider.Mappings.First(a => a.Source?.Id == sourceName).Source;
                    if (source != null)
                    {
                        var prefs = await _suwayomiClient.GetSourcePreferencesAsync(source.Id, token).ConfigureAwait(false);
                        RemoveSuffixPreferences(provider.Lang, source.Id, prefs);
                        sourceDict[source.Id] = prefs;
                    }
                });

            var toUpdate = new List<(string Key, object Value)>();
            foreach (var preference in preferences)
            {
                var currentPref = sourceDict[preference.Source!].FirstOrDefault(a => a.props.key == preference.Key);
                if (currentPref == null || preference.CurrentValue == null)
                    continue;

                if (ShouldUpdatePreference(preference, currentPref))
                {
                    if (preference.CurrentValue.GetType().Name.ToLowerInvariant() == "jsonelement")
                    {
                        preference.CurrentValue = ConvertJsonObject(preference.CurrentValue);
                    }
                    toUpdate.Add((preference.Key, preference.CurrentValue));
                }
            }

            if (toUpdate.Count > 0)
            {
                await UpdatePreferencesInSuwayomiAsync(provider, toUpdate, token).ConfigureAwait(false);
            }
        }

        private bool ShouldUpdatePreference(ProviderPreference preference, SuwayomiPreference currentPref)
        {
            switch (preference.ValueType)
            {
                case ValueType.String:
                    string newValue = (string)ConvertJsonObject(preference.CurrentValue!);
                    string currentValue = (string)(ConvertJsonObject(currentPref.props.currentValue) ?? string.Empty);
                    if (newValue == "!empty-value!" && preference.Type == EntryType.ComboBox)
                        newValue = "";
                    return newValue != currentValue;

                case ValueType.Boolean:
                    bool newBool = (bool)ConvertJsonObject(preference.CurrentValue!);
                    bool currentBool = (bool)(ConvertJsonObject(currentPref.props.currentValue) ?? false);
                    return newBool != currentBool;

                case ValueType.StringCollection:
                    string[] newArray = (string[])ConvertJsonObject(preference.CurrentValue!);
                    string[] currentArray = (string[])(ConvertJsonObject(currentPref.props.currentValue) ?? Array.Empty<string>());
                    return !newArray.SequenceEqual(currentArray);

                default:
                    return false;
            }
        }

        private async Task UpdatePreferencesInSuwayomiAsync(ProviderStorage provider, List<(string Key, object Value)> toUpdate, CancellationToken token)
        {
            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(10);

            foreach (var mapping in provider.Mappings)
            {
                foreach (var update in toUpdate)
                {
                    await semaphore.WaitAsync(token).ConfigureAwait(false);
                    var preference = mapping.Preferences.FirstOrDefault(a => a.props.key == update.Key);
                    if (preference != null)
                    {
                        int index = mapping.Preferences.IndexOf(preference);
                        tasks.Add(Task.Run(async () =>
                        {
                            if (mapping.Source != null)
                            {
                                try
                                {
                                    await _suwayomiClient.SetSourcePreferenceAsync(mapping.Source.Id, index, update.Value, token).ConfigureAwait(false);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }
                        }, token));
                    }
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private object ConvertJsonObject(object obj)
        {
            if (obj is JsonElement str)
            {
                switch (str.ValueKind)
                {
                    case JsonValueKind.String:
                        return str.GetString() ?? string.Empty;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.Array:
                        return JsonSerializer.Deserialize<string[]>(str.GetRawText()) ?? Array.Empty<string>();
                }
            }
            return obj;
        }

        private ProviderPreference ConvertToProviderPreference(SuwayomiPreference p)
        {
            var preference = new ProviderPreference();
            
            switch (p.type)
            {
                case "ListPreference":
                    preference.Type = EntryType.ComboBox;
                    preference.ValueType = ValueType.String;
                    break;
                case "MultiSelectListPreference":
                    preference.Type = EntryType.ComboCheckBox;
                    preference.ValueType = ValueType.StringCollection;
                    break;
                case "SwitchPreferenceCompat":
                case "TwoStatePreference":
                case "CheckBoxPreference":
                    preference.Type = EntryType.Switch;
                    preference.ValueType = ValueType.Boolean;
                    break;
                case "DialogPreference":
                case "EditTextPreference":
                case "Preference":
                case "PreferenceScreen":
                    preference.Type = EntryType.TextBox;
                    preference.ValueType = ValueType.String;
                    break;
            }

            preference.Key = p.props.key;
            preference.CurrentValue = ConvertJsonObject(p.props.currentValue);
            preference.DefaultValue = ConvertJsonObject(p.props.defaultValue);
            preference.Entries = p.props.entries;
            preference.EntryValues = p.props.entryValues;
            preference.Summary = p.props.summary;
            preference.Source = p.Source;
            preference.Title = p.props.title ?? p.props.dialogTitle;

            // Handle empty values in combo boxes
            if (preference.Entries != null && preference.Entries.Count > 0)
            {
                if (preference.EntryValues.Contains(""))
                {
                    preference.EntryValues = preference.EntryValues.Select(a => string.IsNullOrEmpty(a) ? "!empty-value!" : a).ToList();
                    if (preference.CurrentValue is string currentStr && string.IsNullOrEmpty(currentStr))
                        preference.CurrentValue = "!empty-value!";
                    if (preference.DefaultValue is string defaultStr && string.IsNullOrEmpty(defaultStr))
                        preference.DefaultValue = "!empty-value!";
                }

                if (preference.DefaultValue == null)
                    preference.DefaultValue = preference.EntryValues.First();
                if (preference.CurrentValue == null)
                    preference.CurrentValue = preference.DefaultValue;
            }

            return preference;
        }

        private ProviderPreferences ConvertToProviderPreferences(string apkName, List<SuwayomiPreference> prefs)
        {
            return new ProviderPreferences
            {
                ApkName = apkName,
                Preferences = prefs.Select(ConvertToProviderPreference).ToList()
            };
        }

        #endregion
    }
}
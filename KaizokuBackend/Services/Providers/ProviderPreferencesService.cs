using com.sun.xml.@internal.bind.v2.runtime.unmarshaller;
using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Bridge;
using Microsoft.EntityFrameworkCore;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using System.Text.Json;
using ValueType = KaizokuBackend.Models.Enums.ValueType;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider preferences management following SRP
    /// </summary>
    public class ProviderPreferencesService
    {
        private readonly MihonBridgeService _mihon;
        private readonly ProviderCacheService _providerCache;
        private readonly AppDbContext _db;
        private readonly ILogger<ProviderPreferencesService> _logger;

        public ProviderPreferencesService(MihonBridgeService mihon, ProviderCacheService providerCache, AppDbContext db, ILogger<ProviderPreferencesService> logger)
        {
            _mihon = mihon;
            _providerCache = providerCache;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Gets provider preferences by APK name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Provider preferences or null if not found</returns>
        public async Task<ProviderPreferencesDto?> GetProviderPreferencesAsync(string pkgName, CancellationToken token = default)
        {
            try
            {
                var providers = await _providerCache.GetCachedProvidersAsync(token).ConfigureAwait(false);
                if (providers.Count == 0)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", pkgName);
                    return null;
                }
                var provider = providers.FirstOrDefault(a => a.SourcePackageName == pkgName);
                if (provider == null)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", pkgName);
                    return null;
                }
                var repoGroup = _mihon.ListExtensions().FirstOrDefault(a => a.GetActiveEntry().Extension.Package == pkgName);
                if (repoGroup==null)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", pkgName);
                    return null;
                }
                var extInterop = await _mihon.GetInteropAsync(repoGroup, token).ConfigureAwait(false);
                if (extInterop==null)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", pkgName);
                    return null;
                }
                var allPreferences = await extInterop.LoadPreferencesAsync(token).ConfigureAwait(false);
                // Create storage preference
                var storagePreference = CreateStoragePreference(provider);
                var preferences = new List<UniquePreference> { storagePreference };
                preferences.AddRange(allPreferences);
                return ConvertToProviderPreferences(pkgName, provider, preferences);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting provider preferences for {PkgName}", pkgName);
                return null;
            }
        }

        /// <summary>
        /// Sets provider preferences
        /// </summary>
        /// <param name="preferences">Provider preferences to set</param>
        /// <param name="token">Cancellation token</param>
        public async Task SetProviderPreferencesAsync(ProviderPreferencesDto preferences, CancellationToken token = default)
        {
            try
            {
                var providers = await _providerCache.GetCachedProvidersAsync(token).ConfigureAwait(false);
                if (providers.Count == 0)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", preferences.PkgName);
                    return;
                }
                providers = providers.Where(a => a.SourcePackageName == preferences.PkgName).ToList();
                if (providers.Count==0)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", preferences.PkgName);
                    return;
                }
                var repoGroup = _mihon.ListExtensions().FirstOrDefault(a => a.GetActiveEntry().Extension.Package == preferences.PkgName);
                if (repoGroup == null)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", preferences.PkgName);
                    return;
                }
                var extInterop = await _mihon.GetInteropAsync(repoGroup, token).ConfigureAwait(false);
                if (extInterop == null)
                {
                    _logger.LogError("No provider storage found for package '{PkgName}'", preferences.PkgName);
                    return;
                }
                ProviderPreferenceDto isStorage = preferences.Preferences.First(a => a.Index == -1);
                preferences.Preferences.Remove(isStorage);
                var storageValue = (string)ConvertJsonObject(isStorage.CurrentValue!,ValueType.String);
                bool newValue = storageValue == "permanent";
                if (newValue != providers[0].IsStorage) //At this isStorage or not is for all source belonging to an extension.
                {
                    providers.ForEach(a=> a.IsStorage = newValue);
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                    await _providerCache.RefreshCacheAsync(false, token).ConfigureAwait(false);
                }
                var allPreferences = await extInterop.LoadPreferencesAsync(token).ConfigureAwait(false);
                bool change = false;
                foreach(ProviderPreferenceDto p in preferences.Preferences)
                {
                    UniquePreference? u = allPreferences.FirstOrDefault(a => a.Preference.Index == p.Index);
                    if (u!=null)
                    {
                        if (ShouldUpdatePreference(p, u))
                        {
                            object obj = ConvertJsonObject(p.CurrentValue!, p.ValueType);
                            switch(p.ValueType)
                            {
                                case ValueType.String:
                                    u.Preference.CurrentValue = (string)obj;
                                    break;
                                case ValueType.Boolean:
                                    u.Preference.CurrentValue = ((bool)obj) ? "true" : "false";
                                    break;
                                case ValueType.StringCollection:
                                    u.Preference.CurrentValue = JsonSerializer.Serialize((string[])obj);
                                    break;
                            }
                            change = true;
                        }
                    }
                }
                await extInterop.SavePreferencesAsync(allPreferences, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting provider preferences for {PkgName}", preferences.PkgName);
                throw;
            }
        }

        #region Private Helper Methods

        private static UniquePreference CreateStoragePreference(ProviderStorageEntity provider)
        {
            return new UniquePreference
            {
                Languages = new List<KeyLanguage> { new KeyLanguage { Key = "isStorage", Language = "english" } },
                Preference = new Preference
                {
                    Index = -1,
                    Type = "ListPreference",
                    Title = "Provider Download Defaults",
                    Summary = "Permanent providers always download new chapters and replace any existing copies from temporary providers.\nTemporary providers only download a chapter if they are the first to have it available.",
                    Entries = new List<string> { "Permanent", "Temporary" },
                    EntryValues = new List<string> { "permanent", "temporary" },
                    DefaultValueType = "String",
                    DefaultValue = "permanent",
                    CurrentValue = provider.IsStorage ? "permanent" : "temporary"
                }
            };
        }
        /*
        private static List<UniquePreference> OrderByEnglishFirst(List<UniquePreference> mappings)
        {
            var result = new List<UniquePreference>();
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
        */
        private bool ShouldUpdatePreference(ProviderPreferenceDto preference, UniquePreference currentPref)
        {
            switch (preference.ValueType)
            {
                case ValueType.String:
                    string newValue = (string)ConvertJsonObject(preference.CurrentValue!, preference.ValueType);
                    string currentValue = (string)(ConvertJsonObject(currentPref.Preference.CurrentValue, preference.ValueType) ?? string.Empty);
                    if (newValue == "!empty-value!" && preference.Type == EntryType.ComboBox)
                        newValue = "";
                    return newValue != currentValue;

                case ValueType.Boolean:
                    bool newBool = (bool)ConvertJsonObject(preference.CurrentValue!, preference.ValueType);
                    bool currentBool = (bool)(ConvertJsonObject(currentPref.Preference.CurrentValue, preference.ValueType) ?? false);
                    return newBool != currentBool;

                case ValueType.StringCollection:
                    string[] newArray = (string[])ConvertJsonObject(preference.CurrentValue!, preference.ValueType);
                    string[] currentArray = (string[])(ConvertJsonObject(currentPref.Preference.CurrentValue, preference.ValueType) ?? Array.Empty<string>());
                    return !newArray.SequenceEqual(currentArray);

                default:
                    return false;
            }
        }
        /*
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
        */
        private object ConvertJsonObject(object obj, ValueType type)
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
            if (type == ValueType.Boolean && obj is string strb)
            {
                return bool.Parse(strb);
            }
            else if (type == ValueType.Boolean && obj is bool strbo)
            {
                return strbo;
            }
            else if (type== ValueType.StringCollection && obj is string strc)
            {
                return new string[] { strc };
            }
            else if (type==ValueType.String && obj is string strcs)
            {
                return strcs;
            }
            return obj;
        }

        private ProviderPreferenceDto ConvertToProviderPreference(UniquePreference p)
        {
            var preference = new ProviderPreferenceDto();
            
            switch (p.Preference.Type)
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

            preference.Index = p.Preference.Index;
            preference.CurrentValue = ConvertJsonObject(p.Preference.CurrentValue, preference.ValueType);
            preference.DefaultValue = ConvertJsonObject(p.Preference.DefaultValue, preference.ValueType);
            preference.Entries = p.Preference.Entries;
            preference.EntryValues = p.Preference.EntryValues;
            preference.Summary = p.Preference.Summary;
            preference.Title = p.Preference.Title ?? p.Preference.DialogTitle;

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

        private ProviderPreferencesDto ConvertToProviderPreferences(string pkgName, ProviderStorageEntity storage, List<UniquePreference> prefs)
        {
            return new ProviderPreferencesDto
            {
                PkgName = pkgName,
                Preferences = prefs.Select(ConvertToProviderPreference).ToList(),
                Provider = storage.Provider,
                Language = storage.Language,
                Scanlator = storage.Scanlator,
                ThumbnailUrl = storage.ThumbnailUrl,
                IsStorage = storage.IsStorage
            };
        }

        #endregion
    }
}
using eu.kanade.tachiyomi.source;
using java.net;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Loader;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Core.Runtime
{
    /// <summary>
    /// Provides interop capabilities for loading and interacting with Tachiyomi extension assemblies.
    /// Manages the collectible <see cref="AssemblyLoadContext"/> for unloading and creates source bridges.
    /// </summary>
    
    public class ExtensionInterop : IExtensionInterop
    {
        /// <summary>
        /// Logger instance for diagnostics and tracing.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Absolute path to the extension assembly (.dll) being loaded.
        /// </summary>
        private readonly string _assemblyPath;

        /// <summary>
        /// Loaded extension assembly reference. Resolved via the collectible <see cref="PluginLoadContext"/>.
        /// </summary>
        private readonly Assembly _assembly;

        /// <summary>
        /// Collectible plugin load context used to isolate and unload the extension assembly.
        /// </summary>
        private PluginLoadContext? _alc;

        /// <summary>
        /// Weak reference to the plugin load context used to observe unload status.
        /// </summary>
        private WeakReference? _alcWeakRef;


        public string Id => _entry.Id;

        /// <summary>
        /// Gets the human-readable name of the extension.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the version string reported by the extension.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets the list of sources exposed by the extension, wrapped by <see cref="ISourceInterop"/>.
        /// </summary>
        public List<ISourceInterop> Sources { get; }

        // Added fields for preferences context
        private readonly IWorkingFolderStructure _structure;
        private readonly RepositoryEntry _entry;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly List<IDisposable> _disposables = new();

        /// <summary>
        /// Finds concrete types in the loaded assembly that implement or derive from <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The base interface or type to search implementations for.</typeparam>
        /// <returns>An enumerable of matching concrete types.</returns>
        private IEnumerable<Type> GetImplementations<T>()
        {
            var baseType = typeof(T);
            var types = SafeGetTypes(_assembly);
            return types.Where(t =>
                    !t.IsAbstract &&
                    !t.IsInterface &&
                    baseType.IsAssignableFrom(t));
        }

        /// <summary>
        /// Safely retrieves types from the provided assembly, tolerating partial type load failures.
        /// </summary>
        /// <param name="assembly">The assembly to enumerate types from.</param>
        /// <returns>An enumerable of types successfully loaded from the assembly.</returns>
        private IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null)!;
            }
        }

        /// <summary>
        /// Initializes a new instance of <see cref="ExtensionInterop"/>, loads the extension assembly into a collectible
        /// context, discovers its sources, and prepares interop wrappers.
        /// </summary>
        /// <param name="structure">Working folder structure providing the extensions directory.</param>
        /// <param name="entry">Repository entry describing the extension and its DLL file name.</param>
        /// <param name="logger">Logger for diagnostics and error reporting.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="structure"/>, <paramref name="entry"/>, or <paramref name="logger"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when required paths or file names are missing.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the extension assembly cannot be found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the source factory cannot be created or found.</exception>
        public ExtensionInterop(IWorkingFolderStructure structure, RepositoryEntry entry, ILogger logger, string optionalTempPath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (structure == null)
            {
                throw new ArgumentNullException(nameof(structure));
            }

            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            /*
            if (string.IsNullOrWhiteSpace(entry.Dll.FileName))
            {
                throw new ArgumentException("DLL file name is required.", nameof(entry));
            }
            */
            if (string.IsNullOrWhiteSpace(structure.ExtensionsFolder))
            {
                throw new ArgumentException("Extensions folder path is required.", nameof(structure));
            }

         
            _structure = structure;
            _entry = entry;
            string assemblyPath;
            if (optionalTempPath != null)
            {
                assemblyPath = Path.Combine(optionalTempPath, entry./*Dll*/Jar.FileName);
            }
            else
            {
                assemblyPath = Path.Combine(structure.GetExtensionVersionFolder(entry), entry./*Dll*/Jar.FileName);
            }
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("Assembly file not found.", assemblyPath);
            }
            URLClassLoader loader = new URLClassLoader(new URL[0]); // dummy for

            assemblyPath = Path.GetFullPath(assemblyPath);
            _assemblyPath = assemblyPath;
            Name = entry.Name;
            Version = entry.Extension.Version;
            // Collectible ALC => unloadable
            _alc = new PluginLoadContext(_assemblyPath);
            _alcWeakRef = new WeakReference(_alc, trackResurrection: false);
            try
            {
                _assembly = _alc.LoadFromAssemblyPath(_assemblyPath);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading assembly in ExtensionInterop for {ExtensionName}", entry.Name);
                throw;
            }
            object[] srcs;
            Type? sourceFactoryType = GetImplementations<eu.kanade.tachiyomi.source.SourceFactory>().Where(a => (a.FullName ?? "").StartsWith(entry.Extension.Package)).FirstOrDefault();
            if (sourceFactoryType != null)
            {
                SourceFactory? source = (eu.kanade.tachiyomi.source.SourceFactory?)Activator.CreateInstance(sourceFactoryType);
                if (source == null)
                {
                    throw new InvalidOperationException("Could not create SourceFactory instance.");
                }
                srcs = source.createSources().toArray();
                if (srcs.Length == 0)
                {
                    throw new InvalidOperationException("No sources found in SourceFactory.");
                }
            }
            else
            {
                var sourceType = GetImplementations<eu.kanade.tachiyomi.source.Source>().Where(a => (a.FullName ?? "").StartsWith(entry.Extension.Package)).FirstOrDefault();
                if (sourceType==null)
                {
                    throw new InvalidOperationException("No Source implementation found in assembly.");
                }
                var source = Activator.CreateInstance(sourceType);
                if (source==null)
                    throw new InvalidOperationException("Could not create Source instance.");
                srcs = new object[] { source };
            }
                
           
            var list = new List<ISourceInterop>(srcs.Length);
            foreach (var o in srcs)
            {
                var s = (eu.kanade.tachiyomi.source.Source)o;
                list.Add(new SourceInterop(s, logger));
            }

            Sources = list;
        }

        public Task ShutdownAsync(CancellationToken token)
        {
            try
            {
                _shutdownCts.Cancel();
                foreach (var d in _disposables)
                {
                    try { d.Dispose(); } catch { }
                }
                _disposables.Clear();
            }
            catch { }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Loads and merges preferences from both persisted storage and live sources, producing a de-duplicated list
        /// of unique preferences keyed without language suffixes and ordered by language fallback.
        /// </summary>
        /// <param name="token">A cancellation token to observe while awaiting I/O operations.</param>
        /// <returns>A list of unique preferences aggregated across sources.</returns>
        /// <remarks>
        /// This method synchronizes the current source preferences with the persisted preferences.json, applies
        /// language fallback ordering, and strips language-specific suffixes from preference keys to create a unified view.
        /// </remarks>
        public async Task<List<UniquePreference>> LoadPreferencesAsync(CancellationToken token)
        {
            var sourcePrefs = await SyncSourcePreferencesAsync(token).ConfigureAwait(false);
            sourcePrefs = SortPreferencesByLanguageFallback(sourcePrefs);
            Dictionary<string, UniquePreference> uniquePrefs = new Dictionary<string, UniquePreference>();
            foreach (var sp in sourcePrefs)
            {
                foreach (var p in sp.Preferences)
                {
                    string uniqueKey = p.Key;
                    string possibleEnd = "_" + sp.Language;
                    if (uniqueKey.EndsWith(possibleEnd))
                    {
                        uniqueKey = uniqueKey.Substring(0, uniqueKey.Length - possibleEnd.Length);
                    }
                    if (uniquePrefs.ContainsKey(uniqueKey))
                    {
                        uniquePrefs[uniqueKey].Languages.Add(new KeyLanguage { Key = p.Key, Language = sp.Language });
                        continue;
                    }
                    UniquePreference up = new UniquePreference
                    {
                        Languages = new List<KeyLanguage> { new KeyLanguage { Key = p.Key, Language = sp.Language } },
                        Preference = p,
                    };
                    uniquePrefs.Add(uniqueKey, up);
                }
            }
            return uniquePrefs.Values.ToList();
        }
        private string GetPreferencesFilePath(RepositoryEntry entry)
        {
            string preFile = _structure.GetExtensionFolder(_entry);
            return Path.Combine(preFile, "preferences.json");
        }


        /// <summary>
        /// Persists updated preference values into the extension's preferences store and synchronizes them
        /// back to the sources if changes were detected.
        /// </summary>
        /// <param name="press">A list of unique preferences with updated values to save.</param>
        /// <param name="token">A cancellation token to observe while performing file I/O.</param>
        /// <returns>A task representing the asynchronous save operation.</returns>
        /// <remarks>
        /// This method updates matching preferences in the persisted store by comparing keys
        /// (normalized without language suffix) and current values. If changes are detected,
        /// the preferences file is rewritten and sources are synchronized to reflect persisted values.
        /// </remarks>
        public async Task SavePreferencesAsync(List<UniquePreference> press, CancellationToken token)
        {
            string prefs = GetPreferencesFilePath(_entry);
            List<SourcePreference> existing = new List<SourcePreference>();
            if (!File.Exists(prefs))
                return;
            string json = await File.ReadAllTextAsync(prefs, token).ConfigureAwait(false);
            existing = System.Text.Json.JsonSerializer.Deserialize<List<SourcePreference>>(json) ?? new List<SourcePreference>();
            bool change = false;
            Dictionary<string, UniquePreference> keyDict = new Dictionary<string, UniquePreference>();
            foreach(UniquePreference up in press)
            {
                foreach(KeyLanguage kl in up.Languages)
                {
                    keyDict[kl.Key] = up;
                }
            }

            foreach (SourcePreference sp in existing)
            {
                foreach (KeyPreference p in sp.Preferences)
                {
                    if (keyDict.ContainsKey(p.Key))
                    {
                        Preference pr = keyDict[p.Key].Preference;
                        if (p.CurrentValue != pr.CurrentValue)
                        {
                            p.CurrentValue = pr.CurrentValue;
                            change = true;
                        }
                    }
                }
            }
            if (change)
            {
                await SaveInternalSourcePreferencesAsync(existing, token).ConfigureAwait(false);
                await SyncSourcePreferencesAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Writes the provided list of source preferences to the extension's <c>preferences.json</c> file.
        /// </summary>
        /// <param name="press">The list of source preferences to persist.</param>
        /// <param name="token">A cancellation token to observe while writing the file.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        /// <remarks>
        /// Ensures the base folder exists, serializes preferences to JSON, and saves atomically via <see cref="File.WriteAllTextAsync(string,string,System.Threading.CancellationToken)"/>.
        /// </remarks>
        private async Task SaveInternalSourcePreferencesAsync(List<SourcePreference> press, CancellationToken token)
        {
            string prefs = GetPreferencesFilePath(_entry);
            Directory.CreateDirectory(Path.GetDirectoryName(prefs));
            string updatedJson = System.Text.Json.JsonSerializer.Serialize(press);
            await File.WriteAllTextAsync(prefs, updatedJson, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Synchronizes preferences between the live sources and the persisted store, updating sources when necessary
        /// and adding new preferences discovered from sources to the persisted list.
        /// </summary>
        /// <param name="token">A cancellation token to observe during I/O and synchronization.</param>
        /// <returns>The consolidated list of source preferences after synchronization.</returns>
        /// <remarks>
        /// This method loads existing preferences from disk, queries all sources for current preferences,
        /// reconciles differences by:
        /// - Adding newly discovered preferences to the persisted store (ordered by index),
        /// - Pushing persisted values into sources when they differ,
        /// and saves back to disk if changes occurred.
        /// </remarks>
        private async Task<List<SourcePreference>> SyncSourcePreferencesAsync(CancellationToken token)
        {
            string prefs = GetPreferencesFilePath(_entry);
            List<SourcePreference> existing = new List<SourcePreference>();
            if (File.Exists(prefs))
            {
                string json = await File.ReadAllTextAsync(prefs, token).ConfigureAwait(false);
                existing = System.Text.Json.JsonSerializer.Deserialize<List<SourcePreference>>(json) ?? new List<SourcePreference>();
            }

            List<SourcePreference> extprefs = new List<SourcePreference>();
            foreach (ISourceInterop source in Sources)
            {
                var sourcePrefs = source.GetPreferences();
                if (sourcePrefs != null)
                {
                    SourcePreference src = new SourcePreference
                    {
                        SourceId = source.Id,
                        Language = source.Language,
                        Preferences = sourcePrefs
                    };
                    extprefs.Add(src);
                }
            }

            bool needsSave = false;

            foreach (var extPref in extprefs)
            {
                var match = existing.Find(e => e.SourceId == extPref.SourceId);

                if (match == null)
                {
                    existing.Add(extPref);
                    needsSave = true;
                    continue;
                }
                var source = Sources.First(s => s.Id == extPref.SourceId);

                foreach (KeyPreference p in extPref.Preferences)
                {
                    var match2 = match.Preferences.FirstOrDefault(pr => pr.Key == p.Key);
                    if (match2 == null)
                    {
                        match.Preferences.Add(p);
                        match.Preferences = match.Preferences.OrderBy(pr => pr.Index).ToList();
                        needsSave = true;
                    }
                    else if (match2.CurrentValue != p.CurrentValue)
                    {
                        source.SetPreference(p.Index, match2.CurrentValue);
                    }
                }
            }

            if (needsSave)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(prefs));
                string updatedJson = System.Text.Json.JsonSerializer.Serialize(existing);
                await File.WriteAllTextAsync(prefs, updatedJson, token).ConfigureAwait(false);
            }

            return existing;
        }

        /// <summary>
        /// Sorts source preferences using a predefined language fallback order, then by language alphabetically.
        /// </summary>
        /// <param name="prefs">The list of source preferences to order.</param>
        /// <returns>A new list of source preferences ordered by fallback and language.</returns>
        /// <remarks>
        /// The fallback order prioritizes common languages: en, es, fr, de, it, pt, ru, ja, zh.
        /// Unlisted languages are placed after the known set and then ordered alphabetically.
        /// </remarks>
        private static List<SourcePreference> SortPreferencesByLanguageFallback(List<SourcePreference> prefs)
        {
            string[] languageFallbackOrder = new string[] { "en", "es", "fr", "de", "it", "pt", "ru", "ja", "zh" };
            return prefs.OrderBy(p =>
            {
                int index = Array.IndexOf(languageFallbackOrder, p.Language);
                return index >= 0 ? index : int.MaxValue;
            }).ThenBy(p => p.Language).ToList();
        }

        /// <summary>
        /// Disposes the interop by unloading the collectible <see cref="AssemblyLoadContext"/> and
        /// encouraging garbage collection to release resources promptly.
        /// </summary>
        public void Dispose()
        {
            // If already disposed, no-op
            var alc = _alc;
            if (alc is null)
            {
                return;
            }

            // Best-effort shutdown background work
            try { _shutdownCts.Cancel(); } catch { }
            foreach (var d in _disposables)
            {
                try { d.Dispose(); } catch { }
            }
            _disposables.Clear();

            // IMPORTANT: drop strong refs before Unload + GC.
            _alc = null;

            // Initiate unload
            alc.Unload();

            // Encourage prompt unload (it still won't unload if something is referenced)
            for (int i = 0; i < 10 && _alcWeakRef?.IsAlive == true; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            // Optional: if you want to detect leaks, throw here.
            if (_alcWeakRef?.IsAlive == true)
            {
                // Don't throw if you prefer "best effort". If you do want strict behavior:
                // throw new InvalidOperationException("AssemblyLoadContext did not unload. Something is still referenced.");
            }

            _alcWeakRef = null;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Collectible <see cref="AssemblyLoadContext"/> tailored for plugin assembly resolution using
        /// <see cref="AssemblyDependencyResolver"/> to locate dependencies relative to the plugin.
        /// </summary>
        internal sealed class PluginLoadContext : AssemblyLoadContext
        {
            /// <summary>
            /// Resolver used to map assembly names to file paths based on the plugin's main assembly.
            /// </summary>
            private readonly AssemblyDependencyResolver _resolver;

            /// <summary>
            /// Creates a new instance of <see cref="PluginLoadContext"/> with a unique name and collectible behavior.
            /// </summary>
            /// <param name="mainAssemblyPath">Absolute path to the plugin's main assembly.</param>
            public PluginLoadContext(string mainAssemblyPath)
                : base(name: $"plugin:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            }

            /// <summary>
            /// Resolves and loads dependent assemblies for the plugin using the configured resolver.
            /// </summary>
            /// <param name="assemblyName">The name of the requested assembly to load.</param>
            /// <returns>The loaded <see cref="Assembly"/> if resolved; otherwise, <c>null</c>.</returns>
            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var path = _resolver.ResolveAssemblyToPath(assemblyName);
                if (path is null)
                {
                    return null;
                }

                return LoadFromAssemblyPath(path);
            }
        }
    }
}

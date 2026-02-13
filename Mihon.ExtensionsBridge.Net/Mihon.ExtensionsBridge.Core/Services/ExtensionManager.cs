using com.sun.javadoc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using net.dongliu.apk.parser;
using net.dongliu.apk.parser.bean;
using sun.net.www.protocol.jar;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Models;
using Mihon.ExtensionsBridge.Core.Runtime;
using Mihon.ExtensionsBridge.Core.Runtime.Gatekeeper;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using static kotlin.reflect.jvm.@internal.ReflectProperties;
using static kotlinx.coroutines.channels.ChannelResult;
using Mihon.ExtensionsBridge.Core.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Services
{

    /// <summary>
    /// Manages local extension repository groups, download/compile pipeline, and interop lifecycle.
    /// </summary>
    /// <remarks>
    /// This manager coordinates:
    /// <list type="bullet">
    /// <item><description>Discovery and listing of locally available extension repository groups.</description></item>
    /// <item><description>Download of APKs and related artifacts from online repositories.</description></item>
    /// <item><description>DEX to JAR conversion and IKVM compilation pipeline.</description></item>
    /// <item><description>Creation, swapping, and caching of extension interop instances.</description></item>
    /// </list>
    /// Thread-safety: operations that mutate local state are protected by a private lock. Interop instances are cached in a concurrent dictionary.
    /// </remarks>
    public class ExtensionManager : IInternalExtensionManager
    {

        /// <summary>
        /// Logger used for operational and diagnostic messages.
        /// </summary>
        private readonly ILogger _logger;


        public const float LIB_VERSION_MIN = 1.3f;
        public const float LIB_VERSION_MAX = 1.5f;
        /// <summary>
        /// Provides the local working folder structure and persistence of repository groups.
        /// </summary>
        private readonly IWorkingFolderStructure _workingStructure;

        /// <summary>
        /// Converts DEX files to JAR archives as part of the compilation pipeline.
        /// </summary>
        private readonly IDex2JarConverter _dex2Jar;

        /// <summary>
        /// Compiles converted JARs to .NET assemblies using IKVM.
        /// </summary>
       // private readonly IIkvmCompiler _compiler;

  

        private readonly IServiceProvider _serviceProvider;
        private bool _localInitialized = false;

        /// <summary>
        /// Synchronization object used to guard mutations to local extension collections.
        /// </summary>
        private readonly SemaphoreSlim _localExtensionsLock = new(1, 1);


        /// <summary>
        /// Cache of interop instances keyed by <see cref="RepositoryGroup"/> to avoid repeated initialization.
        /// </summary>
        private ConcurrentDictionary<RepositoryGroup, GatekeptExtensionInterop> InteropCache { get; } = new();

        /// <summary>
        /// In-memory list of local extension repository groups. Access guarded by <see cref="_localExtensionsLock"/>.
        /// </summary>
        private List<RepositoryGroup> LocalExtensions { get; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtensionManager"/> class.
        /// </summary>
        /// <param name="workingStructure">Provides working folders and persistence for local repository groups.</param>
        /// <param name="repositoryManager">Resolves online repositories and their extensions.</param>
        /// <param name="repoDownload">Downloader used to fetch extension artifacts.</param>
        /// <param name="dex2jar">Converter for transforming DEX files into JAR files.</param>
        /// <param name="compiler">Compiler that produces .NET assemblies via IKVM.</param>
        /// <param name="logger">Logger for operational diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown if any provided dependency is null.</exception>
        public ExtensionManager(IWorkingFolderStructure workingStructure,
            IRepositoryDownloader repoDownload,
            IDex2JarConverter dex2jar,
            //IIkvmCompiler compiler,            
            IServiceProvider provider,
            ILogger<ExtensionManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workingStructure = workingStructure ?? throw new ArgumentNullException(nameof(workingStructure));
            _dex2Jar = dex2jar ?? throw new ArgumentNullException(nameof(dex2jar));
            //_compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _serviceProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Returns a snapshot list of local extension repository groups.
        /// </summary>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>A task producing a copy of the current local repository groups.</returns>
        /// <remarks>
        /// The returned list is a new instance detached from internal state. Modifying it does not affect stored groups.
        /// </remarks>
        public List<RepositoryGroup> ListExtensions()
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            _localExtensionsLock.Wait();
            try
            {
                return new List<RepositoryGroup>(LocalExtensions);
            }
            finally
            {
                _localExtensionsLock.Release();
            }
        }
        public async Task<RepositoryGroup?> RemoveExtensionVersionAsync(RepositoryEntry entry, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            (RepositoryGroup? group, RepositoryEntry? repoEntry) = await FindRepositoryEntryFromExtensionAsync(entry.Extension, token).ConfigureAwait(false);
            if (group== null || repoEntry == null)
                throw new InvalidOperationException("Specified extension entry not found in local repository groups.");
            await _localExtensionsLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var activeRepoEntry = group.Entries[group.ActiveEntry];
                if (repoEntry==activeRepoEntry)
                {
                    activeRepoEntry = null;
                }
                group.Entries.Remove(repoEntry);
                if (group.Entries.Count == 0)
                {
                    LocalExtensions.Remove(group);
                    group = null;
                }
                else
                {
                    if (activeRepoEntry != null)
                    {
                        group.ActiveEntry = group.Entries.IndexOf(activeRepoEntry);
                    }
                    else
                    {
                        var lastEntry = group.Entries.OrderByDescending(e => e.Extension.VersionCode).First();
                        group.ActiveEntry = group.Entries.IndexOf(lastEntry);
                    }
                }
                if (InteropCache.TryGetValue(group, out var interop))
                {
                    if (interop.Version != repoEntry.Extension.Version)
                    {
                        if (InteropCache.TryRemove(group, out var interop2))
                        {
                            try { interop2.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Error disposing interop during removal for group {GroupName}.", group.Name); }
                        }
                    }
                }
                await _workingStructure.SaveLocalRepositoryGroupsAsync(LocalExtensions, token).ConfigureAwait(false);
            }
            finally
            {
                _localExtensionsLock.Release();
            }
            return group;
        }

        public async Task<RepositoryGroup> SetActiveExtensionVersionAsync(RepositoryGroup group, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            if (group.ActiveEntry < 0 || group.ActiveEntry >= group.Entries.Count)
                throw new InvalidOperationException("Active entry index is out of bounds for repository group.");
            RepositoryEntry current = group.Entries[group.ActiveEntry];
            await _localExtensionsLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (InteropCache.TryGetValue(group, out var interop))
                {
                    if (interop.Version != current.Extension.Version)
                    {
                        if (InteropCache.TryRemove(group, out var interop2))
                        {
                            try { interop2.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Error disposing interop during removal for group {GroupName}.", group.Name); }
                        }
                    }
                }
                await _workingStructure.SaveLocalRepositoryGroupsAsync(LocalExtensions, token).ConfigureAwait(false);
            }
            finally
            {
                _localExtensionsLock.Release();
            }
            return group;
        }
        


        public async Task InitializeAsync(CancellationToken token = default)
        {
            await _localExtensionsLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_localInitialized)
                    return;
                var current = await _workingStructure.LoadLocalRepositoryGroupsAsync(token).ConfigureAwait(false);
                LocalExtensions.Clear();
                if (current!=null)
                    LocalExtensions.AddRange(current);
                _localInitialized = true;
            }
            finally
            {
                _localExtensionsLock.Release();
            }
        }


        /// <summary>
        /// Retrieves an interop instance for the specified repository group, creating and caching it if needed.
        /// </summary>
        /// <param name="entry">The target repository group.</param>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>An initialized interop instance bound to the group's active version.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the active version entry cannot be found.</exception>
        /// <remarks>
        /// If an existing gatekept interop is present but its version differs from the group's active version,
        /// this method swaps the underlying repository entry to keep the interop aligned.
        /// </remarks>
        public async Task<IExtensionInterop> GetInteropAsync(RepositoryGroup entry, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            if (entry.ActiveEntry < 0 || entry.ActiveEntry >= entry.Entries.Count)
                throw new InvalidOperationException("Active entry index is out of bounds for repository group.");
            RepositoryEntry initialEntry = entry.Entries[entry.ActiveEntry];
            try
            {
                GatekeptExtensionInterop interop = InteropCache.GetOrAdd(entry, _ =>
                {
                    GatekeptExtensionInterop created = new GatekeptExtensionInterop(_workingStructure, initialEntry, 
                        (wk, en, log) => {
                            return new JarExtensionInterop(wk, en, log);
                    }, _logger);
                    _logger.LogInformation("Initialized interop for group {GroupName} version {Version}.", entry.Name, initialEntry.Extension.Version);
                    return created;
                });
                if (interop.Id == initialEntry.Id)
                    return interop;
                await interop.SwapAsync(initialEntry, token).ConfigureAwait(false);
                return interop;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Operation canceled while acquiring interop for group {GroupName}.", entry.Name);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or initialize interop for group {GroupName}.", entry.Name);
                throw;
            }
        }
        public RepositoryGroup? FindExtension(RepositoryGroup grp) => FindExtension(grp.Name);
        public RepositoryGroup? FindExtension(string name)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            _localExtensionsLock.Wait();
            try
            {
                foreach (var g in LocalExtensions)
                {
                    if (g.Name == name)
                        return g;
                }
            }
            finally
            {
                _localExtensionsLock.Release();
            }
            return null;
        }

        /// <summary>
        /// Removes a local extension repository group and disposes any cached interop.
        /// </summary>
        /// <param name="group">The repository group to remove.</param>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>True if the group was removed; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="group"/> is null.</exception>
        /// <remarks>
        /// On successful removal, the updated local repository groups are persisted and an informational log entry is emitted.
        /// </remarks>
        public async Task<bool> RemoveExtensionAsync(RepositoryGroup group, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");

            if (group == null) throw new ArgumentNullException(nameof(group));

            bool removed;
            await _localExtensionsLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                removed = LocalExtensions.Remove(group);
            }
            finally
            {
                _localExtensionsLock.Release();
            }

            if (!removed)
                return false;

            if (InteropCache.TryRemove(group, out var interop))
            {
                try { interop.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Error disposing interop during removal for group {GroupName}.", group.Name); }
            }

            try
            {
                await _workingStructure.SaveLocalRepositoryGroupsAsync(LocalExtensions, token).ConfigureAwait(false);
                _logger.LogInformation("Removed local extension group {GroupName}.", group.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Operation canceled while saving groups after removing {GroupName}.", group.Name);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist local repository groups after removing {GroupName}.", group.Name);
                throw;
            }

            return true;
        }

        /// <summary>
        /// Compares online repositories against local active versions and auto-updates out-of-date extensions.
        /// </summary>
        /// <param name="onlineRepos">The set of online repositories to compare against.</param>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>A task that completes when the comparison and potential updates are finished.</returns>
        /// <remarks>
        /// For each extension found online, if a corresponding local group exists and the version differs, an update is scheduled via <see cref="AddExtensionAsync(TachiyomiRepository, TachiyomiExtension, bool, CancellationToken)"/>.
        /// </remarks>
        public async Task CompareOnlineWithLocalAndAutoUpdateAsync(IEnumerable<TachiyomiRepository> onlineRepos, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");

            try
            {
                var lastExtensions = GetLastExtensions();
                foreach (var repo in onlineRepos)
                {
                    foreach (var extension in repo.Extensions)
                    {
                        string folderName = extension.GetName();
                        if (lastExtensions.TryGetValue(folderName, out string? localVersion))
                        {
                            if (localVersion != extension.Version)
                            {
                                _logger.LogInformation("Auto-updating extension {Apk} from version {LocalVersion} to {OnlineVersion}.", extension.Apk, localVersion, extension.Version);
                                try
                                {
                                    await AddExtensionAsync(repo, extension, false, token).ConfigureAwait(false);
                                    _logger.LogInformation("Successfully auto-updated extension {Apk} to version {Version}.", extension.Apk, extension.Version);
                                }
                                catch (OperationCanceledException)
                                {
                                    _logger.LogError("Operation canceled while auto-updating extension {Apk}.", extension.Apk);
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to auto-update extension {Apk} to version {Version}.", extension.Apk, extension.Version);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed during online vs local comparison for auto-update.");
                throw;
            }
        }

        /// <summary>
        /// Adds or updates a local extension by resolving its repository and delegating to the overload that accepts a repository.
        /// </summary>
        /// <param name="extension">The extension to add or update.</param>
        /// <param name="force">If true, forces re-download and recompilation even if an entry exists.</param>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the extension's repository cannot be resolved.</exception>
        public async Task<RepositoryGroup?> AddExtensionAsync(TachiyomiExtension extension, bool force = false, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");

            var repositoryManager = _serviceProvider.GetService<IInternalRepositoryManager>();
            if (repositoryManager == null)
                throw new InvalidOperationException("RepositoryManager service is not available.");
            (TachiyomiRepository? gg, TachiyomiExtension? ext) = repositoryManager.FindRealRepository(extension);
            if(gg == null || ext == null)
                throw new InvalidOperationException("Extension's repository not found in online repositories.");
            return await AddExtensionAsync(gg, ext, force, token).ConfigureAwait(false);
        }
        private static Regex tachiname = new Regex("(.*?)\\.extension\\.(.*)",RegexOptions.Compiled);
        private string CreateApkName(string packagename, string version)
        {
            var match = tachiname.Match(packagename);
            if (match.Success)
            {
                string basepart = match.Groups[1].Value;
                string namepart = match.Groups[2].Value;
                basepart = basepart.Replace("eu.kanade.", "");
                return $"{basepart}-{namepart}-v{version}.apk";
            }
            return $"{packagename}-v{version}.apk";
        }
        private static Regex metaRegex = new Regex(@"<meta-data\b[^>]*android:name\s*=\s*""([^""]+)""[^>]*android:value\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private async Task<ExtensionWorkUnit> WorkUnitFromManifestAsync(ExtensionWorkUnit unit, CancellationToken token = default)
        {
            unit.Entry.DownloadUTC = DateTime.UtcNow;
            string originalName = Path.Combine(unit.WorkingFolder.Path, unit.Entry.Apk.FileName);
            ApkFile z = new ApkFile(originalName);
            ApkMeta meta = z.getApkMeta();
           
            List<string>? features = meta.getUsesFeatures()?.toArray()?.Select(a=>a.ToString()).ToList();
            if (features==null || features.Count==0)
                throw new ArgumentException("APK does not declare any uses-features in its manifest.");
            if (!features.Contains("tachiyomi.extension"))
                throw new ArgumentException("APK is not a valid Tachiyomi extension (missing tachiyomi.extension feature).");
            string version = meta.getVersionName();
            int idx = version.LastIndexOf('.');
            version = idx > 0 ? version.Substring(0, idx) : version;
            if (float.TryParse(version, out float ver))
            {
                if (ver < LIB_VERSION_MIN || ver > LIB_VERSION_MAX)
                    throw new ArgumentException($"APK Tachiyomi library version {ver} is outside the supported range of {LIB_VERSION_MIN} to {LIB_VERSION_MAX}.");
            }
            else
            {
                throw new ArgumentException("APK Tachiyomi library version is not a valid number.");
            }
            unit.Entry.Extension.Name = meta.getLabel();
            unit.Entry.Extension.Name = unit.Entry.Extension.ParsedName();
            unit.Entry.Extension.Package = meta.getPackageName();
            unit.Entry.Extension.Version = meta.getVersionName();
            unit.Entry.Extension.VersionCode = meta.getVersionCode().intValue();
            string xml = z.getManifestXml();

            var dict = metaRegex.Matches(xml)
             .Cast<Match>()
             .ToDictionary(
                 m => m.Groups[1].Value,
                 m => m.Groups[2].Value
             );
            foreach(var f in dict.Keys)
            {
                if (f.Contains(".nsfw"))
                {
                    if (int.TryParse(dict[f], out int nsfw))
                    {
                        unit.Entry.Extension.Nsfw = nsfw;
                    }
                }
                else if (f.Contains(".class"))
                {
                    unit.Entry.ClassName = dict[f];
                }
            }
            string oldApk = unit.Entry.Extension.Apk;
            unit.Entry.Extension.Apk = CreateApkName(unit.Entry.Extension.Package, unit.Entry.Extension.Version);
            if (!oldApk.Equals(unit.Entry.Extension.Apk, StringComparison.OrdinalIgnoreCase))
            {
                string newApkPath = Path.Combine(unit.WorkingFolder.Path, unit.Entry.Extension.Apk);
                File.Move(originalName, newApkPath);
                unit.Entry.Apk.FileName = unit.Entry.Extension.Apk;
            }
            var list = z.getAllIcons();
            byte[]? iconData = GetBestIcon(list, 640);
            if (iconData != null)
            {
                string iconFileName = Path.ChangeExtension(unit.Entry.Extension.Apk, "png");
                string iconPath = Path.Combine(unit.WorkingFolder.Path, iconFileName);
                await File.WriteAllBytesAsync(iconPath, iconData, token).ConfigureAwait(false);
                unit.Entry.Icon = await iconPath.CalculateFileHashAsync(token).ConfigureAwait(false);
            }
            return unit;
        }
        private byte[]? GetBestIcon(java.util.List icons, int maxDensity = int.MaxValue)
        {
            if (icons == null || icons.size() == 0)
                return null;
            List<Icon> icl = new List<Icon>();
            foreach (object o in icons.toArray())
            {
                Icon? i = o as Icon;
                if (i != null && i.getDensity() <= maxDensity)
                    icl.Add(i);
            }
            foreach(object o in icons.toArray())
            {
                AdaptiveIcon? ai = o as AdaptiveIcon;
                if (ai!=null)
                {
                    Icon i = ai.getForeground() ?? ai.getBackground();
                    if (i!=null)
                    {
                        if (i.getDensity() <= maxDensity && !icl.Any(b=>b.getDensity()==i.getDensity()))                                                    
                            icl.Add(i);
                    }
                }
            }
            return icl.OrderByDescending(i => i.getDensity()).FirstOrDefault()?.getData();
        }
        public async Task<RepositoryGroup?> AddExtensionAsync(byte[] apk, bool force = false, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            ExtensionWorkUnit? unitofWork = await CreateWorkUnitFromApkAsync(apk, token).ConfigureAwait(false);
            try
            {
                (RepositoryGroup? group, RepositoryEntry? entry) = await FindRepositoryEntryFromExtensionAsync(unitofWork.Entry.Extension, token).ConfigureAwait(false);
                if (entry != null && !force)
                {
                    _logger.LogInformation("Extension {Apk} version {Version} already present; skipping (force={Force}).", unitofWork.Entry.Extension.Apk, unitofWork.Entry.Extension.Version, force);
                    return group;
                }
                entry = unitofWork.Entry;
                bool result = await CompileAsync(unitofWork, token).ConfigureAwait(false);
                if (!result)
                    return null;
                group = await UpdateEntriesAsync(group, entry, token).ConfigureAwait(false);
                await _workingStructure.SaveLocalRepositoryGroupsAsync(LocalExtensions, token).ConfigureAwait(false);
                _logger.LogInformation("Persisted local repository groups after adding/updating extension {Apk} version {Version}.", unitofWork.Entry.Extension.Apk, unitofWork.Entry.Extension.Version);
                return group;
            }
            finally
            {
                unitofWork?.WorkingFolder.Dispose();
            }
            
        }
        private async Task<ExtensionWorkUnit> CreateWorkUnitFromApkAsync(byte[] apkData, CancellationToken token = default)
        {
            ExtensionWorkUnit unitofWork = new ExtensionWorkUnit();
            unitofWork.Entry = new RepositoryEntry();
            unitofWork.Entry.Extension = new TachiyomiExtension();
            unitofWork.Entry.IsLocal = true;
            unitofWork.WorkingFolder = _workingStructure.CreateTemporaryDirectory();
            string fileName = Path.Combine(unitofWork.WorkingFolder.Path, "extension.apk");
            File.WriteAllBytes(fileName, apkData);
            unitofWork.Entry.Apk = await fileName.CalculateFileHashAsync(token);
            return await WorkUnitFromManifestAsync(unitofWork, token).ConfigureAwait(false);
        }
        private async Task MoveFileSafeAsync(string source, string destination, string error, int retries = 5, CancellationToken token = default)
        {
            bool failed = false;
            if (File.Exists(source))
            {
                do
                {
                    try
                    {
                        File.Move(source, destination, true);
                        failed = false;
                        break;
                    }
                    catch (Exception)
                    {
                        failed = true;
                    }
                    await Task.Delay(500, token).ConfigureAwait(false);
                    retries--;
                } while (retries > 0);
            }
            if (failed)
            {
                _logger.LogWarning(error);
                File.Copy(source, destination, true);
            }
        }

       
        private async Task AcceptWorkUnitAsync(ExtensionWorkUnit workUnit, CancellationToken token = default)
        {
            string expectedFolder = _workingStructure.GetExtensionVersionFolder(workUnit.Entry);
            string srcApkFile = Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Apk.FileName);
            string destApkFile = Path.Combine(expectedFolder, workUnit.Entry.Apk.FileName);
            string srcJarFile = Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Jar.FileName);
            string destJarFile = Path.Combine(expectedFolder, workUnit.Entry.Jar.FileName);
            //string srcDllFile = Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Dll.FileName);
            //string destDllFile = Path.Combine(expectedFolder, workUnit.Entry.Dll.FileName);
            //string srcpdbFile = Path.ChangeExtension(Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Dll.FileName), "pdb");
            //string destpdbFile = Path.ChangeExtension(Path.Combine(expectedFolder, workUnit.Entry.Icon.FileName), "pdb");
            string srcIconFile = Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Icon.FileName);
            string destIconFile = Path.Combine(expectedFolder, workUnit.Entry.Icon.FileName);
            await MoveFileSafeAsync(srcApkFile, destApkFile,
                $"Failed to move APK file for extension {workUnit.Entry.Extension.Name} version {workUnit.Entry.Extension.Version} after multiple attempts. (File Copied)",
                5, token).ConfigureAwait(false);
            await MoveFileSafeAsync(srcJarFile, destJarFile,
                $"Failed to move JAR file for extension {workUnit.Entry.Extension.Name} version {workUnit.Entry.Extension.Version} after multiple attempts. (File Copied)",
                5, token).ConfigureAwait(false);
            await MoveFileSafeAsync(srcIconFile, destIconFile,
                $"Failed to move Icon file for extension {workUnit.Entry.Extension.Name} version {workUnit.Entry.Extension.Version} after multiple attempts. (File Copied)",
                5, token).ConfigureAwait(false);

            //if (File.Exists(srcDllFile))
            //    File.Move(srcDllFile, destDllFile, true);
            //if (File.Exists(srcpdbFile))
            //    File.Move(srcpdbFile, destpdbFile, true);

        }
        private async Task<RepositoryGroup> UpdateEntriesAsync(RepositoryGroup? group, RepositoryEntry entry, CancellationToken token = default)
        {
            await _localExtensionsLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (group == null)
                {
                    group = new RepositoryGroup();
                    group.Name = entry.Name;
                    LocalExtensions.Add(group);
                }
                RepositoryEntry? existingEntry = group.Entries.FirstOrDefault(e => e.Id == entry.Id);
                if (existingEntry != null)
                    group.Entries.Remove(existingEntry);
                group.Entries.Add(entry);
                if (group.AutoUpdate)
                {
                    var lastEntry = group.Entries.OrderByDescending(e => e.Extension.VersionCode).First();
                    group.ActiveEntry = group.Entries.IndexOf(lastEntry);
                }
                return group;
            }
            finally
            {
                _localExtensionsLock.Release();
            }
        }
        private async Task<ExtensionWorkUnit?> Dex2JarAsync(ExtensionWorkUnit unitofWork, CancellationToken token = default)
        {
            bool jared = await _dex2Jar.ConvertAsync(unitofWork, token).ConfigureAwait(false);
            if (!jared)
            {
                _logger.LogError("DEX to JAR conversion failed for extension {Apk} version {Version}.", unitofWork.Entry.Extension.Apk, unitofWork.Entry.Extension.Version);
                return null;
            }
            _logger.LogInformation("Converted DEX to JAR for extension {Apk} version {Version}.", unitofWork.Entry.Extension.Apk, unitofWork.Entry.Extension.Version);
            return unitofWork;
        }
        private async Task<bool> IKVMCompileAsync(ExtensionWorkUnit unitofWork, CancellationToken token = default)
        {

            //await _compiler.CompileAsync(unitofWork, token).ConfigureAwait(false);
            _logger.LogInformation("Compiled extension {Apk} version {Version} to .NET assembly.", unitofWork.Entry.Extension.Apk, unitofWork.Entry.Extension.Version);
            return true;
        }


        private async Task ObtainInformationAsync(ExtensionWorkUnit unitofWork, CancellationToken token = default) 
        {

            using (IInternalExtensionInterop iop = new JarExtensionInterop(_workingStructure, unitofWork.Entry, _logger, unitofWork.WorkingFolder.Path))
            {
            //    ((Action) (() => {
                    string language = "all";
                    string firstLanguage = iop.Sources.First().Language;
                    if (iop.Sources.All(s => s.Language == firstLanguage))
                    {
                        language = firstLanguage;
                    }
                    unitofWork.Entry.Extension.Language = language;
                    List<TachiyomiSource> original_sources = unitofWork.Entry.Extension.Sources.ToList();
                    foreach (SourceInterop sop in iop.Sources)
                    {
                        TachiyomiSource? original = original_sources.FirstOrDefault(s => s.Id == sop.Id.ToString());
                        if (original != null)
                        {
                            original.Language = sop.Language;
                            original.Name = sop.Name;
                            original.BaseUrl = sop.BaseUrl;
                            original.VersionId = sop.VersionId;
                            original_sources.Remove(original);
                        }
                        else
                        {
                            TachiyomiSource newsrc = new TachiyomiSource
                            {
                                Id = sop.Id.ToString(),
                                Name = sop.Name,
                                Language = sop.Language,
                                BaseUrl = sop.BaseUrl,
                                VersionId = sop.VersionId
                            };
                            unitofWork.Entry.Extension.Sources.Add(newsrc);
                        }
                    }
                    foreach (var leftover in original_sources)
                    {
                        _logger.LogWarning("Source {SourceName} (ID: {SourceId}) not found in compiled interop for extension {Apk} version {Version}; removing.", leftover.Name, leftover.Id, unitofWork.Entry.Extension.Apk, unitofWork.Entry.Extension.Version);
                        unitofWork.Entry.Extension.Sources.Remove(leftover);
                    }
              //  })).InvokeInJavaContext();
            }
            await AcceptWorkUnitAsync(unitofWork, token);
           
        }
        private async Task<bool> CompileAsync(ExtensionWorkUnit unitofWork, CancellationToken token = default)
        {
            ExtensionWorkUnit? un = await Dex2JarAsync(unitofWork, token).ConfigureAwait(false);
            if (un == null)
                return false;
            //await IKVMCompileAsync(un, token).ConfigureAwait(false);
           await ObtainInformationAsync(un, token).ConfigureAwait(false);
            return true;
        }



        /// <summary>
        /// Adds or updates a local extension from the specified repository, running download and compilation steps.
        /// </summary>
        /// <param name="repository">The source repository.</param>
        /// <param name="extension">The extension to add or update.</param>
        /// <param name="force">If true, forces re-download and recompilation even if a valid entry exists.</param>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="repository"/> or <paramref name="extension"/> is null.</exception>
        /// <remarks>
        /// Pipeline:
        /// <list type="number">
        /// <item><description>Determine whether an existing entry satisfies requirements; skip if present and <paramref name="force"/> is false.</description></item>
        /// <item><description>Download the extension artifacts via <see cref="_downloader"/>.</description></item>
        /// <item><description>Convert DEX to JAR via <see cref="_dex2Jar"/>.</description></item>
        /// <item><description>Compile to .NET assembly via <see cref="_compiler"/>.</description></item>
        /// <item><description>Update local repository groups, set active version (respecting <c>DoNotAutoUpdate</c>), and persist.</description></item>
        /// </list>
        /// </remarks>
        public async Task<RepositoryGroup?> AddExtensionAsync(TachiyomiRepository repository, TachiyomiExtension extension, bool force = false, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            ExtensionWorkUnit? unitofWork = null;
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (extension == null) throw new ArgumentNullException(nameof(extension));
            try
            {
                (RepositoryGroup? group, RepositoryEntry? entry) = await FindRepositoryEntryFromExtensionAsync(extension, token).ConfigureAwait(false);
                if (entry != null && !force)
                {
                    _logger.LogInformation("Extension {Apk} version {Version} already present; skipping (force={Force}).", extension.Apk, extension.Version, force);
                    return group;
                }
                entry = new RepositoryEntry
                {
                    Name = extension.GetName(),
                    Extension = extension,
                    RepositoryId = repository.Id,
                    IsLocal = false
                };
                unitofWork = new ExtensionWorkUnit();
                unitofWork.Entry = entry;
                unitofWork.WorkingFolder = _workingStructure.CreateTemporaryDirectory();
                using (IServiceScope scope = _serviceProvider.CreateScope())
                {
                    var downloader = scope.ServiceProvider.GetRequiredService<IRepositoryDownloader>();
                    await downloader.DownloadExtensionAsync(repository, unitofWork, token).ConfigureAwait(false);
                }
                _logger.LogInformation("Downloaded extension {Apk} version {Version}.", extension.Apk, extension.Version);
                unitofWork = await WorkUnitFromManifestAsync(unitofWork, token).ConfigureAwait(false);
                bool worked = await CompileAsync(unitofWork, token).ConfigureAwait(false);
                if (!worked)
                    return null;
                group = await UpdateEntriesAsync(group, entry, token).ConfigureAwait(false);
                await _workingStructure.SaveLocalRepositoryGroupsAsync(LocalExtensions, token).ConfigureAwait(false);
                _logger.LogInformation("Persisted local repository groups after adding/updating extension {Apk} version {Version}.", extension.Apk, extension.Version);
                return group;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Operation canceled while adding/updating extension {Apk}.", extension.Apk);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add/update extension {Apk} version {Version}.", extension.Apk, extension.Version);
                throw;
            }
            finally
            {
                unitofWork?.WorkingFolder.Dispose();
            }
        }

        /// <summary>
        /// Builds a mapping of local repository group names to their latest available version strings.
        /// </summary>
        /// <returns>A dictionary where keys are group names and values are latest (highest version code) version strings.</returns>
        /// <remarks>
        /// This information is used to detect mismatches with online repositories for auto-update scenarios.
        /// </remarks>
        private Dictionary<string, string> GetLastExtensions()
        {
            var activeExtensions = new Dictionary<string, string>();
            try
            {
                _localExtensionsLock.Wait();
                try
                {
                    foreach (var group in LocalExtensions)
                    {
                        activeExtensions[group.Name] = group.Entries.OrderByDescending(e => e.Extension.VersionCode).First().Extension.Version;
                    }
                }
                finally
                {
                    _localExtensionsLock.Release();
                }
                _logger.LogInformation("Computed latest local extension versions for {Count} groups.", activeExtensions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compute latest local extension versions.");
                throw;
            }
            return activeExtensions;
        }

        /// <summary>
        /// Searches for a local repository group and entry corresponding to the given extension.
        /// </summary>
        /// <param name="extension">The extension to locate.</param>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item><description>The matching <see cref="RepositoryGroup"/> if found; otherwise, <see langword="null"/>.</description></item>
        /// <item><description>The matching <see cref="RepositoryEntry"/> if present and all required artifacts exist on disk; otherwise, <see langword="null"/>.</description></item>
        /// </list>
        /// </returns>
        /// <remarks>
        /// An entry is considered valid only if its APK, JAR, icon, and compiled DLL files exist under the configured extensions folder.
        /// </remarks>
        private async Task<(RepositoryGroup?, RepositoryEntry?)> FindRepositoryEntryFromExtensionAsync(TachiyomiExtension extension, CancellationToken token = default)
        {
            RepositoryGroup? foundGroup = null;
            string name = extension.GetName();
            try
            {
                await _localExtensionsLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    foreach (RepositoryGroup rg in LocalExtensions)
                    {
                        if (rg.Name == name)
                            foundGroup = rg;
                        foreach (RepositoryEntry entry in rg.Entries)
                        {
                            if (entry.Extension.Apk == extension.Apk)
                            {
                                string expectedFolder = _workingStructure.GetExtensionVersionFolder(entry);

                                if (File.Exists(Path.Combine(expectedFolder, entry.Apk.FileName)) &&
                                    File.Exists(Path.Combine(expectedFolder, entry.Jar.FileName)) &&
                                    File.Exists(Path.Combine(expectedFolder, entry.Icon.FileName)) /*&&
                                    File.Exists(Path.Combine(expectedFolder, entry.Dll.FileName))*/)
                                {
                                    string apkPath = Path.Combine(expectedFolder, entry.Apk.FileName);
                                    FileHash hash = await apkPath.CalculateFileHashAsync(token).ConfigureAwait(false);
                                    if (hash.SHA256 == entry.Apk.SHA256)
                                    {
                                        _logger.LogInformation("Found local entry for extension {Apk} version {Version}.", extension.Apk, entry.Extension.Version);
                                        return (foundGroup, entry);
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _localExtensionsLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for local entry of extension {Apk} version {Version}.", extension.Apk, extension.Version);
                throw;
            }
            return (foundGroup, null);
        }

        public async Task<int> ValidateAndRecompileAsync(IEnumerable<RepositoryEntry> entries, CancellationToken token = default)
        {
            if (!_localInitialized)
                throw new InvalidOperationException("Local extensions not initialized.");
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            int processed = 0;

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();

                // Determine current stored versions on the artifacts
                string expectedFolder = _workingStructure.GetExtensionVersionFolder(entry);

                string jarPath = Path.Combine(expectedFolder, entry.Jar.FileName);
                //string dllPath = Path.Combine(expectedFolder, entry.Dll.FileName);
                string apkPath = Path.Combine(expectedFolder, entry.Apk.FileName);

                // If files are missing, skip this entry gracefully
                if (!File.Exists(jarPath) || !File.Exists(apkPath))
                {
                    _logger.LogInformation("Skipping entry {EntryName}: missing expected artifacts (APK/JAR/DLL) in {Folder}.", entry.Name, expectedFolder);
                    continue;
                }

                bool dex2jarMismatch = !string.Equals(entry.Jar.Version, _dex2Jar.Version, StringComparison.OrdinalIgnoreCase);
                //bool ikvmMismatch = !string.Equals(entry.Dll.Version, _compiler.Version, StringComparison.OrdinalIgnoreCase);

                if (!dex2jarMismatch)// && !ikvmMismatch)
                    continue;

                // Log version mismatch details
                if (dex2jarMismatch)
                {
                    _logger.LogInformation(
                        "DEX2JAR version mismatch for {EntryName}: JAR version {JarVersion} != converter version {Dex2JarVersion}. Recreating JAR and Recompiling DLL.",
                        entry.Name, entry.Jar.Version, _dex2Jar.Version);
                }
                /*
                if (!dex2jarMismatch && ikvmMismatch)
                {
                    _logger.LogInformation(
                        "IKVM compiler version mismatch for {EntryName}: DLL version {DllVersion} != compiler version {CompilerVersion}. Recompiling DLL.",
                        entry.Name, entry.Dll.Version, _compiler.Version);
                }
                */
                // Prepare a work unit from the existing repository entry
                ExtensionWorkUnit unit = new ExtensionWorkUnit
                {
                    Entry = entry,
                    WorkingFolder = _workingStructure.CreateTemporaryDirectory()
                };

                try
                {
                    // Copy required artifacts into the working folder based on the operation needed
                    string workingApk = Path.Combine(unit.WorkingFolder.Path, entry.Apk.FileName);
                    string workingJar = Path.Combine(unit.WorkingFolder.Path, entry.Jar.FileName);

                    if (dex2jarMismatch)
                    {
                        // For full pipeline, we need APK present in working folder
                        File.Copy(apkPath, workingApk, overwrite: true);

                        // Run full compile (Dex2Jar + IKVM)
                        var compiled = await CompileAsync(unit, token).ConfigureAwait(false);
                        if (compiled == null)
                            continue;
                    }
                    /*
                    else if (ikvmMismatch)
                    {
                        // For IKVM only, ensure JAR is available in working folder
                        File.Copy(apkPath, workingApk, overwrite: true);
                        File.Copy(jarPath, workingJar, overwrite: true);

                        var compiled = await IKVMCompileAsync(unit, token).ConfigureAwait(false);
                        if (compiled == null)
                            continue;
                    }
                    */
                    processed++;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("Operation canceled while validating/recompiling entry {EntryName}.", entry.Name);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to validate/recompile entry {EntryName}.", entry.Name);
                }
                finally
                {
                    // Ensure temp is cleaned up if not already moved by AcceptWorkUnit
                    try { unit.WorkingFolder?.Dispose(); } catch { /* ignore */ }
                }
            }

            return processed;
        }

        /// <summary>
        /// Shuts down the manager by disposing all cached interop instances and clearing the interop cache.
        /// </summary>
        /// <param name="token">Cancellation token to observe for early termination.</param>
        /// <returns>A task representing the asynchronous shutdown operation.</returns>
        public async Task ShutdownAsync(CancellationToken token = default)
        {
            try
            {
                // Snapshot keys to avoid enumeration conflicts
                var keys = InteropCache.Keys.ToList();

                foreach (var key in keys)
                {
                    token.ThrowIfCancellationRequested();

                    if (InteropCache.TryRemove(key, out var interop))
                    {
                        try
                        {
                            interop.Dispose();
                            _logger.LogInformation("Disposed interop for group {GroupName}.", key?.Name ?? "<null>");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing interop for group {GroupName}.", key?.Name ?? "<null>");
                        }
                    }
                }

                // Ensure cache is empty
                InteropCache.Clear();

                _logger.LogInformation("Shutdown completed: Interop cache cleared.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Operation canceled during shutdown.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during shutdown.");
                throw;
            }

            await Task.CompletedTask;
        }
    }
}

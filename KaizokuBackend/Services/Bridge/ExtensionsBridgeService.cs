using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using Microsoft.Extensions.Logging;

namespace KaizokuBackend.Services.Bridge;

/// <summary>
/// Provides a high-level facade for interacting with Mihon.ExtensionsBridge components.
/// </summary>
public class ExtensionsBridgeService
{
    private readonly IBridgeManager _bridgeManager;
    private readonly IWorkingFolderStructure _workingFolderStructure;
    private readonly ILogger<ExtensionsBridgeService> _logger;
        private readonly SemaphoreSlim _descriptorLock = new(1, 1);
        private List<BridgeExtensionDescriptor>? _descriptorCache;

    public ExtensionsBridgeService(IBridgeManager bridgeManager, IWorkingFolderStructure workingFolderStructure, ILogger<ExtensionsBridgeService> logger)
    {
        _bridgeManager = bridgeManager ?? throw new ArgumentNullException(nameof(bridgeManager));
        _workingFolderStructure = workingFolderStructure ?? throw new ArgumentNullException(nameof(workingFolderStructure));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns installed extensions with their available sources.
    /// </summary>
        public Task<List<BridgeExtensionDescriptor>> GetInstalledExtensionsAsync(bool forceRefresh = false, CancellationToken token = default)
        {
            return GetExtensionDescriptorsAsync(forceRefresh, token);
        }

        public async Task<BridgeExtensionDescriptor?> GetExtensionDescriptorAsync(string packageId, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("Package id cannot be null or whitespace", nameof(packageId));

            var descriptors = await GetExtensionDescriptorsAsync(false, token).ConfigureAwait(false);
            return descriptors.FirstOrDefault(d => string.Equals(d.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        public Task<TResult?> UseSourceAsync<TResult>(string packageId, long sourceId, Func<ISourceInterop, Task<TResult>> action, CancellationToken token = default)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return UseInteropAsync(packageId, async interop =>
            {
                var source = interop.Sources.FirstOrDefault(s => s.Id == sourceId);
                if (source == null)
                {
                    _logger.LogWarning("Source {SourceId} not found within extension {PackageId}", sourceId, packageId);
                    return default;
                }

                return await action(source).ConfigureAwait(false);
            }, token);
        }

        private async Task<List<BridgeExtensionDescriptor>> GetExtensionDescriptorsAsync(bool forceRefresh, CancellationToken token)
        {
            if (!forceRefresh && _descriptorCache != null)
            {
                return _descriptorCache;
            }

            await _descriptorLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && _descriptorCache != null)
                {
                    return _descriptorCache;
                }

                var descriptors = await BuildDescriptorsAsync(token).ConfigureAwait(false);
                _descriptorCache = descriptors;
                return _descriptorCache;
            }
            finally
            {
                _descriptorLock.Release();
            }
        }

        private async Task<List<BridgeExtensionDescriptor>> BuildDescriptorsAsync(CancellationToken token)
    {
        await EnsureInitializedAsync(token).ConfigureAwait(false);

        var groups = await _bridgeManager.LocalExtensionManager.ListExtensionsAsync(token).ConfigureAwait(false);
        var descriptors = new List<BridgeExtensionDescriptor>(groups.Count);

        foreach (var group in groups)
        {
            var descriptor = await CreateDescriptorAsync(group, token).ConfigureAwait(false);
            if (descriptor != null)
            {
                descriptors.Add(descriptor);
            }
        }

        return descriptors;
    }

    /// <summary>
    /// Executes a function using the interop instance of a specific extension package.
    /// </summary>
    public async Task<TResult?> UseInteropAsync<TResult>(string packageId, Func<IExtensionInterop, Task<TResult>> action, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id cannot be null or whitespace", nameof(packageId));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        await EnsureInitializedAsync(token).ConfigureAwait(false);
        var group = await FindExtensionGroupAsync(packageId, token).ConfigureAwait(false);
        if (group == null)
        {
            _logger.LogWarning("Extension package {Package} was not found in the Mihon bridge", packageId);
            return default;
        }

        IExtensionInterop? interop = null;
        try
        {
            interop = await _bridgeManager.LocalExtensionManager.GetInteropAsync(group, token).ConfigureAwait(false);
            return await action(interop).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing interop action for package {Package}", packageId);
            throw;
        }
        finally
        {
            if (interop != null)
            {
                try
                {
                    await interop.ShutdownAsync(token).ConfigureAwait(false);
                }
                catch (Exception shutdownEx)
                {
                    _logger.LogDebug(shutdownEx, "Interop shutdown failed for package {Package}", packageId);
                }

                interop.Dispose();
            }
        }
    }

    private async Task<BridgeExtensionDescriptor?> CreateDescriptorAsync(RepositoryGroup group, CancellationToken token)
    {
        var activeEntry = group.Entries.ElementAtOrDefault(group.ActiveEntry);
        if (activeEntry?.Extension == null)
        {
            return null;
        }

        IExtensionInterop? interop = null;
        try
        {
            interop = await _bridgeManager.LocalExtensionManager.GetInteropAsync(group, token).ConfigureAwait(false);
            var sources = interop.Sources.Select(source => new BridgeSourceDescriptor
            {
                SourceId = source.Id,
                Name = source.Name,
                Language = source.Language,
                SupportsLatest = source.SupportsLatest,
                IsConfigurable = source.IsConfigurableSource,
                IsHttpSource = source.IsHttpSource,
                IsParsedHttpSource = source.IsParsedHttpSource
            }).ToList();

            string? iconPath = null;
            string? iconHash = activeEntry.Icon?.SHA256;
            if (!string.IsNullOrWhiteSpace(activeEntry.Icon?.FileName))
            {
                var versionFolder = _workingFolderStructure.GetExtensionVersionFolder(activeEntry);
                var candidateIcon = Path.Combine(versionFolder, activeEntry.Icon.FileName);
                if (File.Exists(candidateIcon))
                {
                    iconPath = candidateIcon;
                }
            }

            return new BridgeExtensionDescriptor
            {
                PackageId = activeEntry.Extension.Package,
                RepositoryId = activeEntry.RepositoryId,
                Name = activeEntry.Extension.Name,
                Language = activeEntry.Extension.Language,
                Version = activeEntry.Extension.Version,
                VersionCode = activeEntry.Extension.VersionCode,
                IsNsfw = activeEntry.Extension.Nsfw != 0,
                ApkName = activeEntry.Extension.Apk,
                IconPath = iconPath,
                IconHash = iconHash,
                Sources = sources.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to create descriptor for extension {Extension}", activeEntry.Extension.Package);
            return null;
        }
        finally
        {
            if (interop != null)
            {
                try
                {
                    await interop.ShutdownAsync(token).ConfigureAwait(false);
                }
                catch (Exception shutdownEx)
                {
                    _logger.LogDebug(shutdownEx, "Interop shutdown failed for extension {Extension}", activeEntry.Extension.Package);
                }

                interop.Dispose();
            }
        }
    }

    private async Task<RepositoryGroup?> FindExtensionGroupAsync(string packageId, CancellationToken token)
    {
        var groups = await _bridgeManager.LocalExtensionManager.ListExtensionsAsync(token).ConfigureAwait(false);
        foreach (var group in groups)
        {
            if (group.Entries.Any(entry => string.Equals(entry.Extension?.Package, packageId, StringComparison.OrdinalIgnoreCase)))
            {
                return group;
            }
        }

        return null;
    }

    private async Task EnsureInitializedAsync(CancellationToken token)
    {
        if (_bridgeManager.Initialized)
        {
            return;
        }

        await _bridgeManager.InitializeAsync(token).ConfigureAwait(false);
    }

    private void InvalidateDescriptorCache() => _descriptorCache = null;

    public async Task<List<TachiyomiRepository>> GetOnlineRepositoriesAsync(bool refresh = false, CancellationToken token = default)
    {
        await EnsureInitializedAsync(token).ConfigureAwait(false);
        if (refresh)
        {
            await _bridgeManager.OnlineRepositoryManager.RefreshAllRepositoriesAsync(token).ConfigureAwait(false);
        }

        return await _bridgeManager.OnlineRepositoryManager.ListOnlineRepositoryAsync(token).ConfigureAwait(false);
    }

    public async Task<List<TachiyomiExtension>> GetOnlineExtensionsAsync(bool refresh = false, CancellationToken token = default)
    {
        var repositories = await GetOnlineRepositoriesAsync(refresh, token).ConfigureAwait(false);
        return repositories.SelectMany(r => r.Extensions).ToList();
    }

    public async Task<BridgeExtensionDescriptor?> InstallExtensionAsync(string packageId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id cannot be null or whitespace", nameof(packageId));

        await EnsureInitializedAsync(token).ConfigureAwait(false);
        var match = await FindOnlineExtensionAsync(packageId, token).ConfigureAwait(false)
                   ?? await RefreshAndFindOnlineExtensionAsync(packageId, token).ConfigureAwait(false);

        if (match == null)
        {
            _logger.LogWarning("Package {Package} was not found in any configured repository", packageId);
            return null;
        }

        var (repository, extension) = match.Value;
        var group = await _bridgeManager.LocalExtensionManager.AddExtensionAsync(repository, extension, false, token).ConfigureAwait(false);
        if (group != null)
        {
            InvalidateDescriptorCache();
            return await CreateDescriptorAsync(group, token).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<BridgeExtensionDescriptor?> InstallExtensionFromFileAsync(byte[] apkContent, CancellationToken token = default)
    {
        if (apkContent == null || apkContent.Length == 0)
            throw new ArgumentException("APK content cannot be null or empty", nameof(apkContent));

        await EnsureInitializedAsync(token).ConfigureAwait(false);
        var group = await _bridgeManager.LocalExtensionManager.AddExtensionAsync(apkContent, false, token).ConfigureAwait(false);
        if (group != null)
        {
            InvalidateDescriptorCache();
            return await CreateDescriptorAsync(group, token).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<bool> UninstallExtensionAsync(string packageId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id cannot be null or whitespace", nameof(packageId));

        await EnsureInitializedAsync(token).ConfigureAwait(false);
        var group = await FindExtensionGroupAsync(packageId, token).ConfigureAwait(false);
        if (group == null)
        {
            return false;
        }

        var removed = await _bridgeManager.LocalExtensionManager.RemoveExtensionAsync(group, token).ConfigureAwait(false);
        if (removed)
        {
            InvalidateDescriptorCache();
        }
        return removed;
    }

    public async Task<Stream?> GetExtensionIconStreamAsync(string packageId, CancellationToken token = default)
    {
        var descriptor = await GetExtensionDescriptorAsync(packageId, token).ConfigureAwait(false);
        if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.IconPath) || !File.Exists(descriptor.IconPath))
        {
            return null;
        }

        return new FileStream(descriptor.IconPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public async Task<string?> GetExtensionIconHashAsync(string packageId, CancellationToken token = default)
    {
        var descriptor = await GetExtensionDescriptorAsync(packageId, token).ConfigureAwait(false);
        return descriptor?.IconHash;
    }

    public async Task<List<KeyPreference>> GetSourcePreferencesAsync(string packageId, long sourceId, CancellationToken token = default)
    {
        var preferences = await UseSourceAsync(packageId, sourceId, source =>
        {
            var prefs = source.GetPreferences() ?? new List<KeyPreference>();
            return Task.FromResult(prefs);
        }, token).ConfigureAwait(false);

        return preferences ?? new List<KeyPreference>();
    }

    public async Task<bool> SetSourcePreferenceValueAsync(string packageId, long sourceId, int index, string value, CancellationToken token = default)
    {
        var result = await UseSourceAsync(packageId, sourceId, source =>
        {
            source.SetPreference(index, value ?? string.Empty);
            return Task.FromResult(true);
        }, token).ConfigureAwait(false);

        bool? success = result;
        return success ?? false;
    }

    public async Task<bool> SetSourcePreferenceAsync(string packageId, long sourceId, KeyPreference preference, CancellationToken token = default)
    {
        var result = await UseSourceAsync(packageId, sourceId, source =>
        {
            source.SetPreference(preference);
            return Task.FromResult(true);
        }, token).ConfigureAwait(false);

        bool? success = result;
        return success ?? false;
    }

    private async Task<(TachiyomiRepository Repo, TachiyomiExtension Extension)?> RefreshAndFindOnlineExtensionAsync(string packageId, CancellationToken token)
    {
        await _bridgeManager.OnlineRepositoryManager.RefreshAllRepositoriesAsync(token).ConfigureAwait(false);
        return await FindOnlineExtensionAsync(packageId, token).ConfigureAwait(false);
    }

    private async Task<(TachiyomiRepository Repo, TachiyomiExtension Extension)?> FindOnlineExtensionAsync(string packageId, CancellationToken token)
    {
        var repositories = await _bridgeManager.OnlineRepositoryManager.ListOnlineRepositoryAsync(token).ConfigureAwait(false);
        foreach (var repository in repositories)
        {
            var extension = repository.Extensions.FirstOrDefault(e => packageId.Equals(e.Package, StringComparison.OrdinalIgnoreCase));
            if (extension != null)
            {
                return (repository, extension);
            }
        }

        return null;
    }
}

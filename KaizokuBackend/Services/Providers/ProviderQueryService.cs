using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KaizokuBackend.Models;
using KaizokuBackend.Services.Bridge;
using KaizokuBackend.Services.Helpers;
using Mihon.ExtensionsBridge.Models;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider query operations following SRP
    /// </summary>
    public class ProviderQueryService
    {
        private readonly ProviderCacheService _providerCache;
        private readonly ContextProvider _baseUrl;
        private readonly ExtensionsBridgeService _extensionsBridge;
        private readonly ILogger<ProviderQueryService> _logger;

        public ProviderQueryService(ExtensionsBridgeService extensionsBridge, ProviderCacheService providerCache, ContextProvider baseUrl, ILogger<ProviderQueryService> logger)
        {
            _extensionsBridge = extensionsBridge;
            _providerCache = providerCache;
            _baseUrl = baseUrl;
            _logger = logger;
        }

        /// <summary>
        /// Gets all available extensions (raw list from Suwayomi)
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of available extensions</returns>
        public async Task<List<SuwayomiExtension>> GetExtensionsAsync(CancellationToken token = default)
        {
            try
            {
                return await GetExtensionsWithStatusAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting extensions");
                return [];
            }
        }

        /// <summary>
        /// Gets extensions that have updates available
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of extensions with updates</returns>
        public async Task<List<SuwayomiExtension>> GetExtensionsWithUpdatesAsync(CancellationToken token = default)
        {
            try
            {
                var extensions = await GetExtensionsWithStatusAsync(token).ConfigureAwait(false);
                return extensions.Where(ext => ext.HasUpdate && ext.Installed).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting extensions with updates");
                return [];
            }
        }

        /// <summary>
        /// Gets the icon for an extension by APK name
        /// </summary>
        /// <param name="apkName">The APK name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>File result with the icon</returns>
        public async Task<IActionResult> GetExtensionIconAsync(string apkName, CancellationToken token = default)
        {
            try
            {
                var realPackageId = apkName?.Split('!')[0] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(realPackageId))
                {
                    return new NotFoundResult();
                }

                var iconStream = await _extensionsBridge.GetExtensionIconStreamAsync(realPackageId, token).ConfigureAwait(false);

                if (iconStream == null || iconStream.Length == 0)
                {
                    return new NotFoundResult();
                }

                return new FileStreamResult(iconStream, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting extension icon for {ApkName}", apkName);
                return new StatusCodeResult(500);
            }
        }

        /// <summary>
        /// Gets required extensions for a list of provider infos
        /// </summary>
        /// <param name="ImportProviderSnapshots">List of provider information</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of required extensions</returns>
        public async Task<List<SuwayomiExtension>> GetRequiredExtensionsAsync(List<ImportProviderSnapshot> ImportProviderSnapshots, CancellationToken token = default)
        {
            try
            {
                if (ImportProviderSnapshots == null || ImportProviderSnapshots.Count == 0)
                {
                    return [];
                }

                var extensions = await GetExtensionsWithStatusAsync(token).ConfigureAwait(false);
                var requiredExtensions = new List<SuwayomiExtension>();
                
                var packageIds = ImportProviderSnapshots
                    .Select(p => p.ExtensionPackageId)
                    .Where(pkg => !string.IsNullOrWhiteSpace(pkg))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var packageId in packageIds)
                {
                    var extension = extensions.FirstOrDefault(ext =>
                        ext.PkgName.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
                        !ext.Installed);

                    if (extension != null)
                    {
                        requiredExtensions.Add(extension);
                    }
                }

                var providerNames = ImportProviderSnapshots
                    .Where(p => string.IsNullOrWhiteSpace(p.ExtensionPackageId))
                    .Select(p => p.Provider)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var providerName in providerNames)
                {
                    var extension = extensions.FirstOrDefault(ext =>
                        ext.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase) &&
                        !ext.Installed);

                    if (extension != null)
                    {
                        requiredExtensions.Add(extension);
                    }
                }
                
                return requiredExtensions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting required extensions");
                return [];
            }
        }

        /// <summary>
        /// Gets a list of all available extensions (installed and available to install) with enhanced formatting
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of extensions</returns>
        public async Task<List<SuwayomiExtension>> GetProvidersAsync(CancellationToken token = default)
        {
            try
            {
                await _providerCache.GetCachedProvidersAsync(token).ConfigureAwait(false);
                var extensions = await GetExtensionsWithStatusAsync(token).ConfigureAwait(false);
                return extensions.OrderBy(a => a.Name).ThenBy(a => a.Lang == "all" ? "!" : a.Lang).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving extensions");
                throw;
            }
        }

        private async Task<List<SuwayomiExtension>> GetExtensionsWithStatusAsync(CancellationToken token)
        {
            var installed = await _extensionsBridge.GetInstalledExtensionsAsync(false, token).ConfigureAwait(false);
            var installedLookup = installed.ToDictionary(e => e.PackageId, StringComparer.OrdinalIgnoreCase);

            var repositories = await _extensionsBridge.GetOnlineRepositoriesAsync(false, token).ConfigureAwait(false);
            var results = new List<SuwayomiExtension>();

            foreach (var repository in repositories)
            {
                foreach (var extension in repository.Extensions)
                {
                    installedLookup.TryGetValue(extension.Package, out var descriptor);
                    results.Add(MapExtension(repository, extension, descriptor));
                    if (descriptor != null)
                    {
                        installedLookup.Remove(extension.Package);
                    }
                }
            }

            foreach (var descriptor in installedLookup.Values)
            {
                results.Add(MapInstalledDescriptor(descriptor));
            }

            return results
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(e => Version.Parse(e.VersionName)).Last())
                .ToList();
        }

        private SuwayomiExtension MapExtension(TachiyomiRepository repository, TachiyomiExtension extension, BridgeExtensionDescriptor? descriptor)
        {
            var dto = new SuwayomiExtension
            {
                Repo = repository.Name,
                Name = extension.Name,
                PkgName = extension.Package,
                ExtensionPackageId = extension.Package,
                ExtensionRepositoryId = repository.Id,
                VersionName = extension.Version,
                VersionCode = extension.VersionCode,
                Lang = extension.Language,
                ApkName = descriptor?.ApkName ?? extension.Apk,
                IsNsfw = extension.Nsfw != 0,
                Installed = descriptor != null,
                HasUpdate = descriptor != null && extension.VersionCode > descriptor.VersionCode,
                IconUrl = descriptor != null ? $"/icon/{descriptor.PackageId}" : "/icon/unknown",
                Obsolete = false
            };

            dto.IconUrl = _baseUrl.RewriteExtensionIcon(dto);
            dto.Sources = descriptor != null
                ? MapSourcesFromDescriptor(descriptor)
                : MapSourcesFromRepository(extension);
            return dto;
        }

        private SuwayomiExtension MapInstalledDescriptor(BridgeExtensionDescriptor descriptor)
        {
            var dto = new SuwayomiExtension
            {
                Repo = "local",
                Name = descriptor.Name,
                PkgName = descriptor.PackageId,
                ExtensionPackageId = descriptor.PackageId,
                ExtensionRepositoryId = descriptor.RepositoryId,
                VersionName = descriptor.Version,
                VersionCode = descriptor.VersionCode,
                Lang = descriptor.Language,
                ApkName = descriptor.ApkName,
                IsNsfw = descriptor.IsNsfw,
                Installed = true,
                HasUpdate = false,
                IconUrl = $"/icon/{descriptor.PackageId}",
                Obsolete = false,
                Sources = MapSourcesFromDescriptor(descriptor)
            };

            dto.IconUrl = _baseUrl.RewriteExtensionIcon(dto);
            return dto;
        }

        private static List<ExtensionSourceSummary> MapSourcesFromRepository(TachiyomiExtension extension)
        {
            if (extension.Sources == null || extension.Sources.Count == 0)
            {
                return [];
            }

            var summaries = new List<ExtensionSourceSummary>(extension.Sources.Count);
            foreach (var source in extension.Sources)
            {
                if (!long.TryParse(source.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
                {
                    continue;
                }

                summaries.Add(new ExtensionSourceSummary
                {
                    SourceId = parsedId,
                    Name = source.Name,
                    Lang = source.Language,
                    SupportsLatest = false,
                    IsConfigurable = false
                });
            }

            return summaries;
        }

        private static List<ExtensionSourceSummary> MapSourcesFromDescriptor(BridgeExtensionDescriptor descriptor)
        {
            if (descriptor.Sources == null || descriptor.Sources.Count == 0)
            {
                return [];
            }

            return descriptor.Sources.Select(source => new ExtensionSourceSummary
            {
                SourceId = source.SourceId,
                Name = source.Name,
                Lang = source.Language,
                SupportsLatest = source.SupportsLatest,
                IsConfigurable = source.IsConfigurable
            }).ToList();
        }
    }
}

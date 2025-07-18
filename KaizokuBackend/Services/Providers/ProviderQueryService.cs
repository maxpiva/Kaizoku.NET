using KaizokuBackend.Models;
using KaizokuBackend.Services.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider query operations following SRP
    /// </summary>
    public class ProviderQueryService
    {
        private readonly SuwayomiClient _suwayomiClient;
        private readonly ProviderCacheService _providerCache;
        private readonly ContextProvider _baseUrl;
        private readonly ILogger<ProviderQueryService> _logger;

        public ProviderQueryService(SuwayomiClient suwayomiClient, ProviderCacheService providerCache, ContextProvider baseUrl, ILogger<ProviderQueryService> logger)
        {
            _suwayomiClient = suwayomiClient;
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
                return await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
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
                var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
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
                var iconStream = await _suwayomiClient.GetExtensionIconAsync(apkName, token).ConfigureAwait(false);
                
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
        /// <param name="providerInfos">List of provider information</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of required extensions</returns>
        public async Task<List<SuwayomiExtension>> GetRequiredExtensionsAsync(List<ProviderInfo> providerInfos, CancellationToken token = default)
        {
            try
            {
                var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
                var requiredExtensions = new List<SuwayomiExtension>();
                
                var providerNames = providerInfos.Select(p => p.Provider).Distinct().ToList();
                
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
                var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);

                // Update icon URLs to point to the API base URL
                foreach (var extension in extensions)
                {
                    extension.IconUrl = _baseUrl.RewriteExtensionIcon(extension);
                }

                // Group extensions by name to show only the latest version
                var groupedExtensions = extensions
                    .GroupBy(e => e.Name)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Keep only the latest version of each extension
                var latestExtensions = new List<SuwayomiExtension>();
                foreach (var group in groupedExtensions.Values)
                {
                    var latest = group.OrderBy(a => Version.Parse(a.VersionName)).Last();
                    latestExtensions.Add(latest);
                }

                // Sort extensions by name and then by language, with 'all' languages first
                return latestExtensions.OrderBy(a => a.Name).ThenBy(a => a.Lang == "all" ? "!" : a.Lang).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving extensions");
                throw;
            }
        }
    }
}
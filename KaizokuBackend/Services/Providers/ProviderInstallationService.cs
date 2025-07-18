using KaizokuBackend.Models;
using KaizokuBackend.Services.Providers;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider installation and uninstallation operations following SRP
    /// </summary>
    public class ProviderInstallationService
    {
        private readonly SuwayomiClient _suwayomiClient;
        private readonly ProviderCacheService _providerCache;
        private readonly ILogger<ProviderInstallationService> _logger;

        public ProviderInstallationService(SuwayomiClient suwayomiClient, ProviderCacheService providerCache, ILogger<ProviderInstallationService> logger)
        {
            _suwayomiClient = suwayomiClient;
            _providerCache = providerCache;
            _logger = logger;
        }

        /// <summary>
        /// Installs an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if installation was successful</returns>
        public async Task<bool> InstallProviderAsync(string pkgName, CancellationToken token = default)
        {
            try
            {
                _logger.LogInformation("Installing provider: {PkgName}", pkgName);
                bool result = await _suwayomiClient.InstallExtensionAsync(pkgName, token).ConfigureAwait(false);
                
                if (result)
                {
                    // Update cache after successful installation
                    var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
                    var installedExtension = extensions.FirstOrDefault(e => e.PkgName == pkgName);
                    if (installedExtension != null)
                    {
                        await _providerCache.UpdateExtensionAsync(installedExtension, token).ConfigureAwait(false);
                    }
                    
                    _logger.LogInformation("Provider {PkgName} installed successfully", pkgName);
                }
                else
                {
                    _logger.LogWarning("Failed to install provider: {PkgName}", pkgName);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error installing provider {PkgName}", pkgName);
                return false;
            }
        }

        /// <summary>
        /// Installs an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if installation was successful</returns>
        public async Task<string?> InstallProviderFromFileAsync(byte[] content, string fileName, CancellationToken token = default)
        {
            try
            {
                _logger.LogInformation("Installing provider: {fileName}", fileName);
                bool result = await _suwayomiClient.InstallExtensionFromFileAsync(content, fileName, token).ConfigureAwait(false);

                if (result)
                {
                    // Update cache after successful installation
                    var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
                    var installedExtension = extensions.FirstOrDefault(e => e.ApkName == fileName);
                    if (installedExtension != null)
                    {
                        await _providerCache.UpdateExtensionAsync(installedExtension, token).ConfigureAwait(false);
                        _logger.LogInformation("Provider {PkgName} installed successfully", installedExtension.PkgName);
                        return installedExtension.PkgName;
                    }

                }
                else
                {
                    _logger.LogWarning("Failed to install provider: {fileName}", fileName);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error installing provider {fileName}", fileName);
            }
            return null;
        }


        /// <summary>
        /// Uninstalls an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if uninstallation was successful</returns>
        public async Task<bool> UninstallProviderAsync(string pkgName, CancellationToken token = default)
        {
            try
            {
                _logger.LogInformation("Uninstalling provider: {PkgName}", pkgName);
                bool result = await _suwayomiClient.UninstallExtensionAsync(pkgName, token).ConfigureAwait(false);
                
                if (result)
                {
                    // Update cache after successful uninstallation
                    var extensions = await _suwayomiClient.GetExtensionsAsync(token).ConfigureAwait(false);
                    var uninstalledExtension = extensions.FirstOrDefault(e => e.PkgName == pkgName);
                    if (uninstalledExtension != null)
                    {
                        await _providerCache.RemoveExtensionAsync(uninstalledExtension, token).ConfigureAwait(false);
                    }
                    
                    _logger.LogInformation("Provider {PkgName} uninstalled successfully", pkgName);
                }
                else
                {
                    _logger.LogWarning("Failed to uninstall provider: {PkgName}", pkgName);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uninstalling provider {PkgName}", pkgName);
                return false;
            }
        }
    }
}
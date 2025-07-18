using KaizokuBackend.Services.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for provider resource delivery following SRP
    /// </summary>
    public class ProviderResourceService
    {
        private readonly SuwayomiClient _suwayomiClient;
        private readonly EtagCacheService _etagCacheService;
        private readonly ILogger<ProviderResourceService> _logger;

        // Base64 unknown icon
        private const string UnknownIconBase64 = "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";

        public ProviderResourceService(SuwayomiClient suwayomiClient, EtagCacheService etagCacheService, ILogger<ProviderResourceService> logger)
        {
            _suwayomiClient = suwayomiClient;
            _etagCacheService = etagCacheService;
            _logger = logger;
        }

        /// <summary>
        /// Gets the icon for a provider with ETag caching
        /// </summary>
        /// <param name="apkName">APK name of the extension</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Icon as a stream result</returns>
        public Task<IActionResult> GetProviderIconAsync(string apkName, CancellationToken token = default)
        {
            string[] split = apkName.Split('!');
            string realApkName = split.Length > 1 ? split[0] : apkName;

            return _etagCacheService.ETagWrapperAsync(apkName, async () =>
            {
                if (realApkName == "unknown")
                {
                    return new MemoryStream(Convert.FromBase64String(UnknownIconBase64));
                }

                try
                {
                    return await _suwayomiClient.GetExtensionIconAsync(realApkName, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get icon for {ApkName}, returning unknown icon", realApkName);
                    return new MemoryStream(Convert.FromBase64String(UnknownIconBase64));
                }
            }, token);
        }
    }
}
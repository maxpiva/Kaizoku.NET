using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Services.Bridge;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Providers
{
    /// <summary>
    /// Service for checking provider health by performing test searches
    /// </summary>
    public class ProviderHealthCheckService
    {
        private readonly AppDbContext _db;
        private readonly MihonBridgeService _mihon;
        private readonly ProviderCacheService _providerCache;
        private readonly ILogger<ProviderHealthCheckService> _logger;

        public ProviderHealthCheckService(
            AppDbContext db,
            MihonBridgeService mihon,
            ProviderCacheService providerCache,
            ILogger<ProviderHealthCheckService> logger)
        {
            _db = db;
            _mihon = mihon;
            _providerCache = providerCache;
            _logger = logger;
        }

        /// <summary>
        /// Health-checks a single provider by attempting a test search
        /// </summary>
        public async Task<ProviderHealthResultDto> CheckProviderAsync(string mihonProviderId, CancellationToken token = default)
        {
            var provider = await _db.Providers.FirstOrDefaultAsync(p => p.MihonProviderId == mihonProviderId, token)
                .ConfigureAwait(false);

            if (provider == null)
            {
                return new ProviderHealthResultDto
                {
                    MihonProviderId = mihonProviderId,
                    Passed = false,
                    Error = "Provider not found",
                    CheckedAtUtc = DateTime.UtcNow
                };
            }

            return await RunHealthCheckAsync(provider, skipCacheRefresh: false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Health-checks all sources belonging to a specific extension package
        /// </summary>
        public async Task<List<ProviderHealthResultDto>> CheckByPackageAsync(string packageName, CancellationToken token = default)
        {
            var providers = await _db.Providers
                .Where(p => p.SourcePackageName == packageName && p.IsEnabled && !p.IsDead)
                .ToListAsync(token).ConfigureAwait(false);

            if (providers.Count == 0)
            {
                return [new ProviderHealthResultDto
                {
                    MihonProviderId = packageName,
                    Passed = false,
                    Error = "No enabled sources found for this package",
                    CheckedAtUtc = DateTime.UtcNow
                }];
            }

            var results = new List<ProviderHealthResultDto>();
            foreach (var provider in providers)
            {
                if (token.IsCancellationRequested) break;
                var result = await RunHealthCheckAsync(provider, skipCacheRefresh: true, token).ConfigureAwait(false);
                results.Add(result);
            }

            await _providerCache.RefreshCacheAsync(true, token).ConfigureAwait(false);
            return results;
        }

        /// <summary>
        /// Health-checks all enabled/installed providers
        /// </summary>
        public async Task<List<ProviderHealthResultDto>> CheckAllProvidersAsync(CancellationToken token = default)
        {
            var providers = await _db.Providers
                .Where(p => p.IsEnabled && !p.IsDead)
                .ToListAsync(token).ConfigureAwait(false);

            var results = new List<ProviderHealthResultDto>();

            // Check providers sequentially to avoid overloading sources
            foreach (var provider in providers)
            {
                if (token.IsCancellationRequested) break;

                var result = await RunHealthCheckAsync(provider, skipCacheRefresh: true, token).ConfigureAwait(false);
                results.Add(result);
            }

            // Single cache refresh after all checks complete
            await _providerCache.RefreshCacheAsync(true, token).ConfigureAwait(false);

            return results;
        }

        /// <summary>
        /// Runs the actual health check on a single provider — tries to initialize
        /// the source interop and perform a minimal search.
        /// </summary>
        private async Task<ProviderHealthResultDto> RunHealthCheckAsync(ProviderStorageEntity provider, bool skipCacheRefresh = false, CancellationToken token = default)
        {
            var result = new ProviderHealthResultDto
            {
                MihonProviderId = provider.MihonProviderId,
                Name = provider.Name,
                Language = provider.Language
            };

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

                // Step 1: Try to get the source interop (tests extension loading)
                var source = await _mihon.SourceFromProviderIdAsync(provider.MihonProviderId, timeoutCts.Token)
                    .ConfigureAwait(false);

                // Step 2: Try a minimal search (tests the actual source/website)
                var searchResult = await source.SearchAsync(1, "a", timeoutCts.Token).ConfigureAwait(false);

                // If we get here without exception, the source is healthy
                result.Passed = true;
                result.CheckedAtUtc = DateTime.UtcNow;

                _logger.LogInformation("Health check PASSED for provider {Name} ({Id})",
                    provider.Name, provider.MihonProviderId);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                result.Passed = false;
                result.Error = "Timed out after 15 seconds";
                result.CheckedAtUtc = DateTime.UtcNow;

                _logger.LogWarning("Health check FAILED for provider {Name} ({Id}): Timeout",
                    provider.Name, provider.MihonProviderId);
            }
            catch (HttpRequestException ex)
            {
                result.Passed = false;
                result.Error = $"HTTP error: {ex.StatusCode}";
                result.CheckedAtUtc = DateTime.UtcNow;

                _logger.LogWarning("Health check FAILED for provider {Name} ({Id}): HTTP {StatusCode}",
                    provider.Name, provider.MihonProviderId, ex.StatusCode);
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Error = ex.Message;
                result.CheckedAtUtc = DateTime.UtcNow;

                _logger.LogWarning(ex, "Health check FAILED for provider {Name} ({Id}): {Message}",
                    provider.Name, provider.MihonProviderId, ex.Message);
            }

            // Persist health check result to DB
            provider.LastHealthCheckUtc = result.CheckedAtUtc;
            provider.LastHealthCheckPassed = result.Passed;
            provider.LastHealthCheckError = result.Passed ? null : result.Error;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Refresh cache so the UI picks up the new status (skip when batching)
            if (!skipCacheRefresh)
            {
                await _providerCache.RefreshCacheAsync(true, token).ConfigureAwait(false);
            }

            return result;
        }
    }
}

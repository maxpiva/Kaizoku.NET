using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Search;
using RensaioBackend.Services.Search.Discovery;
using RensaioBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc;

namespace RensaioBackend.Controllers
{
    /// <summary>
    /// Controller for searching series across multiple sources
    /// </summary>
    [ApiController]
    [Route("api/search")]
    public class SearchController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly SearchQueryService _searchQueryService;
        private readonly SearchCommandService _searchCommandService;
        private readonly DiscoverySearchService _discoverySearchService;
        private readonly DiscoverySearchCoordinator _discoveryCoordinator;
        private readonly ThumbCacheService _thumbs;
        private readonly SettingsService _settings;

        public SearchController(
            ILogger<SearchController> logger,
            SearchQueryService searchQueryService,
            SearchCommandService searchCommandService,
            DiscoverySearchService discoverySearchService,
            DiscoverySearchCoordinator discoveryCoordinator,
            ThumbCacheService thumbs,
            SettingsService settingsService)
        {
            _searchQueryService = searchQueryService;
            _searchCommandService = searchCommandService;
            _discoverySearchService = discoverySearchService;
            _discoveryCoordinator = discoveryCoordinator;
            _settings = settingsService;
            _thumbs = thumbs;
            _logger = logger;
        }

        private static List<string> ParseLanguages(string? languages)
        {
            return (languages ?? string.Empty).Split(',')
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
        }
        /// <summary>
        /// Augments a list of linked series with full details and type information
        /// </summary>
        /// <param name="linkedSeries">List of linked series to augment</param>
        /// <returns>List of full series with complete information</returns>
        /// <remarks>
        /// This endpoint retrieves detailed information for each series, including metadata,
        /// descriptions, authors, and automatically categorizes them based on genre.
        /// </remarks>
        [HttpPost("augment")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AugmentedResponseDto>> AugmentSeriesAsync([FromBody] List<LinkedSeriesDto> linkedSeries, CancellationToken token = default)
        {
            try
            {
                if (linkedSeries == null || linkedSeries.Count == 0)
                {
                    return BadRequest(new { error = "No series provided to augment" });
                }

                var augmentedSeries = await _searchCommandService.AugmentSeriesAsync(linkedSeries, token).ConfigureAwait(false);
                await _thumbs.PopulateThumbsAsync(augmentedSeries.Series, "/api/image/", token).ConfigureAwait(false);
                return Ok(augmentedSeries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error augmenting series");
                return StatusCode(500, new { error = "An error occurred while augmenting series" });
            }
        }

        /// <summary>
        /// Gets all available search sources based on preferred languages
        /// </summary>
        /// <returns>List of available search sources</returns>
        [HttpGet("sources")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<List<SearchSourceDto>>> GetAvailableSearchSourcesAsync(CancellationToken token = default)
        {
            try
            {
                var sources = await _searchQueryService.GetAvailableSearchSourcesAsync(token).ConfigureAwait(false);
                return Ok(sources.OrderBy(a=>a.Provider).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving search sources: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while retrieving search sources" });
            }
        }

        /// <summary>
        /// Starts (or attaches to) an automatic streaming discovery sweep for the query. Returns
        /// immediately: when done=true the results are complete (cache hit / disabled / nothing
        /// eligible); otherwise the client keeps searchId and listens for "DiscoverySearch" events
        /// on the /progress SignalR hub for incremental results and per-extension progress.
        /// </summary>
        [HttpPost("discovery/start")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<DiscoveryStartDto>> StartDiscoveryAsync(
            [FromBody] DiscoveryStartRequestDto request,
            CancellationToken token = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Keyword))
            {
                return BadRequest("Search keyword is required");
            }
            try
            {
                var result = await _discoveryCoordinator.StartOrAttachAsync(request.Keyword, request.Languages, token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting discovery search: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while starting the discovery search" });
            }
        }

        /// <summary>
        /// Requests detail augmentation (chapter count + status) for the next batch of cached
        /// discovery results that lack them; updates stream as "details" SignalR events.
        /// </summary>
        [HttpPost("discovery/details")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> StartDiscoveryDetailsAsync(
            [FromBody] DiscoveryStartRequestDto request,
            [FromQuery] int count = DiscoverySearchCoordinator.DefaultDetailsCount,
            CancellationToken token = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Keyword))
            {
                return BadRequest("Search keyword is required");
            }
            try
            {
                int queued = await _discoveryCoordinator.StartDetailsAsync(request.Keyword, request.Languages, count, token).ConfigureAwait(false);
                return Ok(new { queued });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting discovery detail augmentation: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while loading discovery details" });
            }
        }

        /// <summary>
        /// Cancels an in-flight discovery sweep (retyped query or closed dialog); its worker
        /// processes are terminated and its partial results are discarded.
        /// </summary>
        [HttpPost("discovery/cancel/{searchId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult CancelDiscovery([FromRoute] string searchId)
        {
            bool cancelled = _discoveryCoordinator.Cancel(searchId);
            return Ok(new { cancelled });
        }

        /// <summary>
        /// Gets the number of not-installed extensions/sources eligible for discovery search,
        /// so the UI can show "Search N more sources".
        /// </summary>
        /// <param name="languages">Comma-separated list of language codes (defaults to preferred languages)</param>
        [HttpGet("discovery/sources")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<DiscoverySourcesDto>> GetDiscoverySourcesAsync(
            [FromQuery] string? languages = null,
            CancellationToken token = default)
        {
            try
            {
                var result = await _discoverySearchService.GetDiscoverySourcesAsync(ParseLanguages(languages), token).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving discovery sources: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while retrieving discovery sources" });
            }
        }

        /// <summary>
        /// Searches for series across sources whose extensions are NOT installed, by shadow-loading
        /// them server-side. Results carry installed=false plus the extension package/repository so
        /// the client can install the extension when a result is selected.
        /// </summary>
        /// <param name="keyword">Search keyword</param>
        /// <param name="languages">Comma-separated list of language codes (defaults to preferred languages)</param>
        /// <remarks>
        /// The first search against a given extension downloads and converts its APK, which can take
        /// a while; subsequent searches reuse the shadow-loaded extension.
        /// </remarks>
        [HttpGet("discovery")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<List<DiscoverySeriesDto>>> SearchDiscoveryAsync(
            [FromQuery] string keyword,
            [FromQuery] string? languages = null,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest("Search keyword is required");
            }
            try
            {
                var results = await _discoverySearchService.SearchSeriesAsync(keyword, ParseLanguages(languages), 0.1f, token).ConfigureAwait(false);
                await _thumbs.PopulateThumbsAsync(results, "/api/image/", token).ConfigureAwait(false);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in discovery search: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while searching not-installed sources" });
            }
        }

        /// <summary>
        /// Searches for series across multiple sources
        /// </summary>
        /// <param name="keyword">Search keyword</param>
        /// <param name="languages">Comma-separated list of language codes to search in (e.g. "en,ja,ko")</param>
        /// <param name="searchSources">Optional list of specific source IDs to search</param>
        /// <returns>List of series matching the search criteria</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<List<LinkedSeriesDto>>> SearchSeriesAsync(
            [FromQuery] string keyword,
            [FromQuery] string? languages = null, 
            [FromQuery] List<string>? searchSources = null, 
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest("Search keyword is required");
            }
            
            if (string.IsNullOrEmpty(languages))
                languages = string.Join(',', (await _settings.GetSettingsAsync(token).ConfigureAwait(false)).PreferredLanguages);

            // Parse languages from comma-separated string
            var languageList = languages.Split(',')
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            try
            {
                var results = await _searchQueryService.SearchSeriesAsync(keyword, languageList, searchSources, 0.1f, token).ConfigureAwait(false);
                await _thumbs.PopulateThumbsAsync(results, "/api/image/", token).ConfigureAwait(false);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching series: {Message}", ex.Message);
                return StatusCode(500, new { error = "An error occurred while searching series" });
            }
        }
    }
}

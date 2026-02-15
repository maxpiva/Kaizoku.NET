using KaizokuBackend.Models.Dto;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Search;
using KaizokuBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc;

namespace KaizokuBackend.Controllers
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
        private readonly ThumbCacheService _thumbs;
        private readonly SettingsService _settings;
        
        public SearchController(
            ILogger<SearchController> logger, 
            SearchQueryService searchQueryService,
            SearchCommandService searchCommandService,
            ThumbCacheService thumbs,
            SettingsService settingsService) 
        {
            _searchQueryService = searchQueryService;
            _searchCommandService = searchCommandService;
            _settings = settingsService;
            _thumbs = thumbs;
            _logger = logger;
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

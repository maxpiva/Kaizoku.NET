using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mihon.ExtensionsBridge.Core.Models;
using Mihon.ExtensionsBridge.Core.Abstractions;


namespace Mihon.ExtensionsBridge.Core.Services
{
    /// <summary>
    /// Provides functionality to download and populate Tachiyomi extensions from a remote repository endpoint.
    /// </summary>
    /// <remarks>
    /// This service fetches a JSON payload from the specified repository URL, deserializes it into a collection of
    /// <see cref="TachiyomiExtension"/> instances, and persists the result using the configured working folder structure.
    /// </remarks>
    public class RepositoryDownloader : IRepositoryDownloader
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWorkingFolderStructure _folder;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RepositoryDownloader"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The HTTP client factory used to create configured <see cref="HttpClient"/> instances.</param>
        /// <param name="folder">The working folder structure used to persist repository data.</param>
        /// <param name="logger">The logger used to write diagnostic and operational logs.</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the provided arguments are <c>null</c>.</exception>
        public RepositoryDownloader(IHttpClientFactory httpClientFactory, IWorkingFolderStructure folder, ILogger<RepositoryDownloader> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates and configures an HTTP client for downloading repository data.
        /// </summary>
        /// <returns>A configured <see cref="HttpClient"/> instance with a custom user agent and timeout.</returns>
        /// <remarks>
        /// The client is created via <see cref="IHttpClientFactory"/> to leverage central configuration and lifetime management.
        /// A product-specific user agent is attached and a five-minute timeout is set to accommodate large payloads.
        /// </remarks>
        private HttpClient CreateHttpClient()
        {
            var client = _httpClientFactory.CreateClient(nameof(RepositoryDownloader));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ExtensionBridge", "1.0"));
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }


        private static string[] index = ["index.min.json", "index.json"];

        private static string[] repos = ["repo.json"];

        /// <summary>
        /// Downloads, deserializes, and persists Tachiyomi extensions for the provided repository.
        /// </summary>
        /// <param name="repository">The repository descriptor containing the source URL and metadata to populate.</param>
        /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
        /// <returns>The updated <see cref="TachiyomiRepository"/> instance containing extensions and last update timestamp.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="repository"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when the repository URL is <c>null</c>, empty, or whitespace.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="cancellationToken"/>.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request is unsuccessful.</exception>
        /// <exception cref="JsonException">Thrown when the JSON payload cannot be deserialized.</exception>
        /// <remarks>
        /// This method:
        /// - Validates input arguments.
        /// - Performs an HTTP GET to the repository URL.
        /// - Streams and deserializes the JSON payload into <see cref="List{T}"/> of <see cref="TachiyomiExtension"/>.
        /// - Updates the repository with the extensions and the current UTC timestamp.
        /// - Persists the repository using <see cref="IWorkingFolderStructure.SaveExtensionAsync(TachiyomiRepository, CancellationToken)"/>
        /// </remarks>
        public async Task<TachiyomiRepository> PopulateExtensionsAsync(TachiyomiRepository repository, CancellationToken cancellationToken = default)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrWhiteSpace(repository.Url)) throw new ArgumentException("Repository URL cannot be null or whitespace.", nameof(repository));
            _logger.LogInformation("Starting repository download from URL: {RepositoryUrl}", repository.Url);

            HttpResponseMessage? response = null;
            string? usedUrl = null;

            try
            {
                var client = CreateHttpClient();

                foreach (var fileName in repos)
                {
                    var candidateUrl = repository.Url.CombineUrl(fileName);
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidateUrl);
                    var tempResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                    if (tempResponse.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        tempResponse.Dispose();
                        _logger.LogDebug("Index not found at {CandidateUrl}. Trying next candidate...", candidateUrl);
                        continue;
                    }
                    response = tempResponse;
                    usedUrl = candidateUrl;
                    break;
                }
                if (response!=null)
                {
                    await using var rstream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    var reposMeta = await JsonSerializer.DeserializeAsync<RepositoryMeta>(rstream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }, cancellationToken).ConfigureAwait(false);
                    if (reposMeta != null)
                    {
                        repository.WebSite = reposMeta.meta.website;
                        repository.Name = reposMeta.meta.name;
                        repository.Fingerprint = reposMeta.meta.signingKeyFingerprint;
                    }
                }

                // Try index candidates first
                foreach (var fileName in index)
                {
                    var candidateUrl = repository.Url.CombineUrl(fileName);
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidateUrl);
                    var tempResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                    if (tempResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        tempResponse.Dispose();
                        _logger.LogDebug("Index not found at {CandidateUrl}. Trying next candidate...", candidateUrl);
                        continue;
                    }

                    tempResponse.EnsureSuccessStatusCode();
                    response = tempResponse;
                    usedUrl = candidateUrl;
                    break;
                }
                // Fallback to original URL if no index candidate succeeded
                if (response == null)
                {
                    throw new HttpRequestException("No valid index file found in the repository.");
                }

                _logger.LogInformation("Resolved repository index at: {ResolvedUrl}", usedUrl);

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var extensions = await JsonSerializer.DeserializeAsync<List<TachiyomiExtension>>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }, cancellationToken).ConfigureAwait(false);

                repository.Extensions = extensions ?? new List<TachiyomiExtension>();
                repository.LastUpdatedUTC = DateTimeOffset.UtcNow;

                _logger.LogInformation("Downloaded {ExtensionCount} extensions from {ResolvedUrl}. Saving to working folder...", repository.Extensions.Count, usedUrl);
                await _folder.SaveOnlineRepositoryAsync(repository, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Completed extension download and save for {ResolvedUrl}.", usedUrl);

                return repository;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Extension download canceled for {RepositoryUrl}.", repository.Url);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading extensions from {RepositoryUrl}.", repository.Url);
                throw;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Downloads the APK and icon for a specific extension into the working folder structure.
        /// </summary>
        /// <param name="repository">The repository that hosts the extension artifacts.</param>
        /// <param name="extension">The extension metadata used to resolve artifact names and version.</param>
        /// <param name="force">If true, forces re-download even if files already exist.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public async Task DownloadExtensionAsync(TachiyomiRepository repository, ExtensionWorkUnit workUnit, CancellationToken cancellationToken = default)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (workUnit == null) throw new ArgumentNullException(nameof(workUnit));
            if (workUnit.Entry==null) throw new ArgumentException("Extension entry cannot be null.", nameof(workUnit));
            if (workUnit.Entry.Extension == null) throw new ArgumentException("Extension metadata cannot be null.", nameof(workUnit));
            if (string.IsNullOrWhiteSpace(repository.Url)) throw new ArgumentException("Repository URL cannot be null or whitespace.", nameof(repository));
            if (string.IsNullOrWhiteSpace(workUnit.Entry.Extension.Apk)) throw new ArgumentException("Extension APK cannot be null or whitespace.", nameof(workUnit));
            if (string.IsNullOrWhiteSpace(workUnit.Entry.Extension.Version)) throw new ArgumentException("Extension Version cannot be null or whitespace.", nameof(workUnit));
            if (string.IsNullOrWhiteSpace(workUnit.Entry.Extension.Package)) throw new ArgumentException("Extension Package cannot be null or whitespace.", nameof(workUnit));

            var apkUrl = repository.Url.CombineUrl("apk", workUnit.Entry.Extension.Apk);
           
            var apkDestination = Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Extension.Apk);
            var client = CreateHttpClient();
            try
            {
                await DownloadWithLoggingAsync(client, label: "APK", apkUrl, apkDestination, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download canceled for {Package} v{Version}.", workUnit.Entry.Extension.Package, workUnit.Entry.Extension.Version);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading artifacts for {Package} v{Version}.", workUnit.Entry.Extension.Package, workUnit.Entry.Extension.Version);
                throw;
            }
            workUnit.Entry.Name = workUnit.Entry.Extension.GetName();
            workUnit.Entry.DownloadUTC = DateTimeOffset.UtcNow;
            workUnit.Entry.DownloadUrl = apkUrl;
            workUnit.Entry.Apk = await apkDestination.CalculateFileHashAsync(cancellationToken).ConfigureAwait(false);
          
        }

        private async Task DownloadWithLoggingAsync(HttpClient httpClient, string label, string url, string destination, CancellationToken ct)
        {
            _logger.LogInformation("Downloading {Label} from {Url} -> {Destination}", label, url, destination);
            try
            {
                await DownloadFileAsync(httpClient, url, destination, ct).ConfigureAwait(false);
                _logger.LogInformation("Downloaded {Label} to {Destination}", label, destination);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download {Label} from {Url}", label, url);
                throw;
            }
        }



        private static async Task DownloadFileAsync(HttpClient client, string url, string destinationPath, CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await network.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }
}
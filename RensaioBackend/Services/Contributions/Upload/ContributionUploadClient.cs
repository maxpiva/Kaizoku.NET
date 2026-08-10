using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RensaioBackend.Services.Contributions.Upload;

/// <summary>
/// Thin HTTP client for maxpiva's contribution worker. Auth is the contributor UUID as a
/// query parameter, so the UUID is a secret: it must never appear in logs — this class
/// logs status codes and scrubbed messages only, and the named client's built-in
/// request-URI logging is removed at registration.
/// </summary>
public sealed class ContributionUploadClient
{
    public const string HttpClientName = "ContributionUpload";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<ContributionUploadClient> _logger;

    public ContributionUploadClient(IHttpClientFactory factory, ILogger<ContributionUploadClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<ContributorProbeResult> GetContributorAsync(string baseUrl, string contributorUuid,
        CancellationToken token = default)
    {
        if (!TryBuildUri(baseUrl, "contributor", contributorUuid, out Uri? uri, out string? uriError))
            return new ContributorProbeResult { Status = ContributionCallStatus.RetryableError, Error = uriError };
        try
        {
            HttpClient client = _factory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client
                .GetAsync(uri, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ContributorProbeResult { Status = ContributionCallStatus.UnknownContributor };
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new ContributorProbeResult
                {
                    Status = ContributionCallStatus.Banned,
                    Error = await ReadBanReasonAsync(response, token).ConfigureAwait(false)
                };
            if (!response.IsSuccessStatusCode)
                return new ContributorProbeResult
                {
                    Status = ContributionCallStatus.RetryableError,
                    Error = $"Contribution worker returned {(int)response.StatusCode}."
                };
            ContributorResponse? contributor = await response.Content
                .ReadFromJsonAsync<ContributorResponse>(JsonOptions, token).ConfigureAwait(false);
            return new ContributorProbeResult
            {
                Status = ContributionCallStatus.Success,
                Contributor = contributor ?? new ContributorResponse()
            };
        }
        catch (Exception ex) when (IsTransport(ex, token))
        {
            _logger.LogWarning("Contribution worker contributor probe failed: {Message}", ex.Message);
            return new ContributorProbeResult { Status = ContributionCallStatus.RetryableError, Error = ex.Message };
        }
    }

    public async Task<UploadCallResult> UploadAsync(string baseUrl, string contributorUuid,
        IReadOnlyList<UploadItem> items, CancellationToken token = default)
    {
        if (!TryBuildUri(baseUrl, "upload", contributorUuid, out Uri? uri, out string? uriError))
            return new UploadCallResult { Status = ContributionCallStatus.RetryableError, Error = uriError };
        try
        {
            HttpClient client = _factory.CreateClient(HttpClientName);
            string body = JsonSerializer.Serialize(new UploadRequest { Items = items.ToList() }, JsonOptions);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client
                .PostAsync(uri, content, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UploadCallResult { Status = ContributionCallStatus.UnknownContributor };
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new UploadCallResult
                {
                    Status = ContributionCallStatus.Banned,
                    Error = await ReadBanReasonAsync(response, token).ConfigureAwait(false)
                };
            if (!response.IsSuccessStatusCode)
                return new UploadCallResult
                {
                    Status = ContributionCallStatus.RetryableError,
                    Error = $"Contribution worker returned {(int)response.StatusCode}."
                };
            UploadResponse? parsed = await response.Content
                .ReadFromJsonAsync<UploadResponse>(JsonOptions, token).ConfigureAwait(false);
            return new UploadCallResult
            {
                Status = ContributionCallStatus.Success,
                Response = parsed ?? new UploadResponse()
            };
        }
        catch (Exception ex) when (IsTransport(ex, token))
        {
            _logger.LogWarning("Contribution upload batch failed: {Message}", ex.Message);
            return new UploadCallResult { Status = ContributionCallStatus.RetryableError, Error = ex.Message };
        }
    }

    /// <summary>
    /// Builds the request URI without throwing: a malformed stored base URL (e.g. a pasted
    /// endpoint link that slipped past settings-save normalization, or garbage in the database
    /// from before that normalization existed) must surface as a normal
    /// <see cref="ContributionCallStatus.RetryableError"/> result, not an unhandled
    /// <see cref="UriFormatException"/> that would crash the request.
    /// </summary>
    private static bool TryBuildUri(string baseUrl, string path, string contributorUuid,
        out Uri? uri, out string? error)
    {
        uri = null;
        string root = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (Uri.TryCreate($"{root}/{path}?contributor={Uri.EscapeDataString(contributorUuid)}",
                UriKind.Absolute, out Uri? built) &&
            (built.Scheme == Uri.UriSchemeHttp || built.Scheme == Uri.UriSchemeHttps))
        {
            uri = built;
            error = null;
            return true;
        }
        error = "The worker URL is not a valid absolute http(s) URL.";
        return false;
    }

    private static async Task<string?> ReadBanReasonAsync(HttpResponseMessage response, CancellationToken token)
    {
        // 403 body: {"error":"Contributor is banned: <reason>"}
        try
        {
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                string? message = error.GetString();
                if (message is null)
                    return null;
                const string prefix = "Contributor is banned:";
                int index = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                return index >= 0 ? message[(index + prefix.Length)..].Trim() : message;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>
    /// Transport-level failures (connection refused, DNS, timeout, malformed response body)
    /// are retryable; a caller-initiated cancellation is not and propagates.
    /// </summary>
    private static bool IsTransport(Exception ex, CancellationToken token)
        => ex is HttpRequestException or JsonException or InvalidOperationException
           || (ex is TaskCanceledException && !token.IsCancellationRequested);
}

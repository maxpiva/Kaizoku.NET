using System.Net;
using System.Text.Json;

namespace RensaioBackend.Services.Contributions.Snapshot;

/// <summary>
/// Thin HTTP client for the public contribution snapshot export. The snapshot files carry no
/// credentials, so no request-URI scrubbing is needed here; the decryption key is fetched from
/// the contribution worker's public <c>/key</c> endpoint (also credential-free).
/// </summary>
public sealed class ContributionSnapshotClient
{
    public const string HttpClientName = "ContributionSnapshot";

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<ContributionSnapshotClient> _logger;

    public ContributionSnapshotClient(IHttpClientFactory factory, ILogger<ContributionSnapshotClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<SnapshotFileResult> GetFileAsync(string baseUrl, string fileName, string? etag,
        CancellationToken token = default)
    {
        try
        {
            HttpClient client = _factory.CreateClient(HttpClientName);
            string root = (baseUrl ?? string.Empty).TrimEnd('/');
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{root}/{fileName}");
            // The stored ETag is the normalized header value (already quoted, W/ prefix intact);
            // add it verbatim so weak tags round-trip without re-parsing.
            if (!string.IsNullOrEmpty(etag))
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
                return new SnapshotFileResult { Status = SnapshotFetchStatus.NotModified };
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new SnapshotFileResult { Status = SnapshotFetchStatus.NotFound };
            if (!response.IsSuccessStatusCode)
                return new SnapshotFileResult
                {
                    Status = SnapshotFetchStatus.RetryableError,
                    Error = $"Snapshot export returned {(int)response.StatusCode} for {fileName}."
                };

            byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            return new SnapshotFileResult
            {
                Status = SnapshotFetchStatus.Success,
                Body = body,
                ETag = response.Headers.ETag?.ToString()
            };
        }
        catch (Exception ex) when (IsTransport(ex, token))
        {
            _logger.LogWarning("Snapshot export fetch of {File} failed: {Message}", fileName, ex.Message);
            return new SnapshotFileResult { Status = SnapshotFetchStatus.RetryableError, Error = ex.Message };
        }
    }

    public async Task<SnapshotKeyResult> GetKeyAsync(string workerBaseUrl, CancellationToken token = default)
    {
        try
        {
            HttpClient client = _factory.CreateClient(HttpClientName);
            string root = (workerBaseUrl ?? string.Empty).TrimEnd('/');
            using HttpResponseMessage response = await client.GetAsync($"{root}/key", token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new SnapshotKeyResult { Error = $"Contribution worker returned {(int)response.StatusCode} for the decryption key." };

            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            byte[] material;
            try
            {
                material = Convert.FromBase64String(body.Trim());
            }
            catch (FormatException)
            {
                return new SnapshotKeyResult { Error = "The decryption key response was not valid base64." };
            }
            if (material.Length != 48)
                return new SnapshotKeyResult { Error = $"The decryption key material was {material.Length} bytes; expected 48." };

            return new SnapshotKeyResult
            {
                Success = true,
                Key = material[0..32],
                Iv = material[32..48]
            };
        }
        catch (Exception ex) when (IsTransport(ex, token))
        {
            _logger.LogWarning("Contribution worker key fetch failed: {Message}", ex.Message);
            return new SnapshotKeyResult { Error = ex.Message };
        }
    }

    /// <summary>
    /// Transport-level failures (connection refused, DNS, timeout, malformed response body)
    /// are retryable; a caller-initiated cancellation is not and propagates.
    /// </summary>
    private static bool IsTransport(Exception ex, CancellationToken token)
        => ex is HttpRequestException or JsonException or InvalidOperationException
           || (ex is TaskCanceledException && !token.IsCancellationRequested);
}

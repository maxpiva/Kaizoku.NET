using KaizokuBackend.Extensions;
using KaizokuBackend.Models;

namespace KaizokuBackend.Services.Helpers
{
    public class ContextProvider
    {
        private readonly HttpRequest? _request;
        private readonly HttpResponse? _response;

        public ContextProvider(IHttpContextAccessor httpContextAccessor)
        {
            _request = httpContextAccessor?.HttpContext?.Request;
            _response = httpContextAccessor?.HttpContext?.Response;
            if (_request != null)
            {
                string requestUrl = $"{_request.Scheme}://{_request.Host}{_request.Path}";
                int idx = requestUrl.LastIndexOf("/api/", StringComparison.InvariantCulture);
                BaseUrl = idx > 0 ? requestUrl.Substring(0, idx + 5) : requestUrl;
            }
            else
                BaseUrl = "";
        }


        public string BaseUrl { get; }


        /// <summary>
        /// Gets the ETag value from the request's If-None-Match header
        /// </summary>
        /// <returns>The ETag value if present, null otherwise</returns>
        internal string? GetETagFromRequest()
        {
            if (_request == null)
                return null;
            if (_request.Headers.TryGetValue("If-None-Match", out var etagValues))
            {
                string etag = etagValues.ToString();

                // If the ETag is wrapped in quotes, remove them
                if (etag.StartsWith("\"") && etag.EndsWith("\""))
                {
                    etag = etag.Substring(1, etag.Length - 2);
                }

                return etag;
            }

            return null;
        }

        /// <summary>
        /// Adds an ETag header to the response
        /// </summary>
        /// <param name="etag">The ETag value to add</param>
        internal void AddETag(string etag)
        {
            if (!string.IsNullOrEmpty(etag))
            {
                // Add the ETag header, properly quoted as per HTTP spec
                string quotedEtag = $"\"{etag}\"";
                if (_response != null)
                {
                    _response.Headers.ETag = quotedEtag;
                    _response.Headers.CacheControl = "public, max-age=86400"; // Cache for 1 day
                    _response.Headers.Expires = DateTime.UtcNow.AddDays(1).ToString("R");
                    _response.Headers.Remove("Pragma");
                    _response.Headers.Remove("Vary");
                }
            }
        }

    }
}
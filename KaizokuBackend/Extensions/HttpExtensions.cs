namespace KaizokuBackend.Extensions
{
    public static class HttpExtensions
    {
        /// <summary>
        /// Gets the ETag value from the request's If-None-Match header
        /// </summary>
        /// <returns>The ETag value if present, null otherwise</returns>
        public static string? GetETagFromRequest(this HttpRequest? request)
        {
            if (request == null)
                return null;
            if (request.Headers.TryGetValue("If-None-Match", out var etagValues))
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
        public static void AddETag(this HttpResponse? response, TimeSpan timespan, string etag)
        {
            if (!string.IsNullOrEmpty(etag))
            {
                int secs = (int)timespan.TotalSeconds;
                // Add the ETag header, properly quoted as per HTTP spec
                string quotedEtag = $"\"{etag}\"";
                if (response != null)
                {
                    response.Headers.ETag = quotedEtag;
                    response.Headers.CacheControl = $"public, max-age={secs}"; // Cache for 1 day
                    response.Headers.Expires = DateTime.UtcNow.AddSeconds(secs).ToString("R");
                    response.Headers.Remove("Pragma");
                    response.Headers.Remove("Vary");
                }
            }
        }
    }
}

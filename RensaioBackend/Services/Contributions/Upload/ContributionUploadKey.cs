using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RensaioBackend.Services.Contributions.Upload;

/// <summary>
/// Computes the contribution database's source identity: lowercase-hex
/// MD5(UTF8("{packageName}:{sourceId}:{url}")) where the url is the source-relative
/// (unprefixed) manga url exactly as collected. This is the wire identity used by
/// maxpiva's contribution worker and is distinct from the internal SHA-256
/// <see cref="ContributionIdentity"/> used by the local collector store.
/// </summary>
public static class ContributionUploadKey
{
    public static string Create(ContributionRecordV1 record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Create(record.Package, record.SourceId, record.Url);
    }

    public static string Create(string package, long sourceId, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        string identity = package + ":" + sourceId.ToString(CultureInfo.InvariantCulture) + ":" + url;
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}

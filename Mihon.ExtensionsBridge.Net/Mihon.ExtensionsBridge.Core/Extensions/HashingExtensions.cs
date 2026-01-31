using System;
using System.Collections.Generic;
using System.Text;
using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Core.Extensions
{
    public static class HashingExtensions
    {
        public static string SHA256FromUrl(string url)
        {
            url = url.ToUpperInvariant();
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public static async Task<string> CalculateFileHashStringAsync(this string filePath, CancellationToken token = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] data = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
            var hashBytes = sha256.ComputeHash(data);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        public static async Task<FileHash> CalculateFileHashAsync(this string filePath, CancellationToken token = default)
        {
            return new FileHash
            {
                FileName = Path.GetFileName(filePath),
                SHA256 = await filePath.CalculateFileHashStringAsync(token).ConfigureAwait(false)
            };
        }
        public static async Task<FileHashVersion> CalculateFileHashAsync(this string filePath, string version, CancellationToken token = default)
        {
            return new FileHashVersion
            {
                FileName = Path.GetFileName(filePath),
                SHA256 = await filePath.CalculateFileHashStringAsync(token).ConfigureAwait(false),
                Version = version
            };
        }


    }
}

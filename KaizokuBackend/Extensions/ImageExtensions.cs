using KaizokuBackend.Services.Images;
using SkiaSharp;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for image processing and manipulation
    /// </summary>
    public static class ImageExtensions
    {
        private static readonly List<(byte[] Signature, int Offset, string MimeType, string Extension)> ImageSignatures = new()
        {
            (new byte[] { 0xFF, 0xD8 }, 0, "image/jpeg", ".jpg"),
            (new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 0, "image/png", ".png"),
            (new byte[] { 0x47, 0x49, 0x46, 0x38 }, 0, "image/gif", ".gif"),
            (new byte[] { 0x42, 0x4D }, 0, "image/bmp", ".bmp"),
            (new byte[] { 0x00, 0x00, 0x01, 0x00 }, 0, "image/x-icon", ".ico"),
            (new byte[] { 0x49, 0x49, 0x2A, 0x00 }, 0, "image/tiff", ".tiff"),
            (new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, 0, "image/tiff", ".tiff"),
            (new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, 0, "image/webp", ".webp"),
            (new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20 }, 0, "image/jxl", ".jxl"),
            (new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20 }, 0, "image/jp2", ".jp2"),
            (new byte[] { 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 }, 4, "image/avif", ".avif"),
            (new byte[] { 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63 }, 4, "image/heic", ".heic")
        };

        /// <summary>
        /// Detects image MIME type and file extension from a stream
        /// </summary>
        /// <param name="stream">Stream to analyze</param>
        /// <returns>Tuple containing MIME type and file extension, or null values if not detected</returns>
        public static (string? MimeType, string? Extension) GetImageMimeTypeAndExtension(this Stream stream)
        {
            if (!stream.CanRead || !stream.CanSeek)
                return (null, null);

            byte[] header = new byte[20];
            int _ = stream.Read(header, 0, header.Length);
            stream.Position = 0;

            foreach (var (signature, offset, mime, ext) in ImageSignatures)
            {
                if (header.Length >= offset + signature.Length)
                {
                    bool match = true;
                    for (int i = 0; i < signature.Length; i++)
                    {
                        // 0 byte in signature acts as wildcard
                        if (signature[i] != 0 && header[offset + i] != signature[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                        return (mime, ext);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Writes a cover.jpg file from an image stream to the specified folder path.
        /// </summary>
        /// <param name="imageStream">The source image stream (seekable)</param>
        /// <param name="folderPath">The target folder path where cover.jpg will be written</param>
        /// <param name="jpegQuality">JPEG quality (0-100, default 90)</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>true if the cover was written successfully, false otherwise</returns>
        public static async Task<bool> WriteCoverJpegAsync(this Stream imageStream, string folderPath, int jpegQuality = 90, CancellationToken token = default)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string coverPath = Path.Combine(folderPath, "cover.jpg");

                // Reset stream position to the beginning
                imageStream.Position = 0;

                // Detect image format using the existing extension method
                var (mimeType, _) = imageStream.GetImageMimeTypeAndExtension();

                // Reset stream position after detection
                imageStream.Position = 0;

                // If it's already JPEG, write directly
                if (mimeType == "image/jpeg")
                {
                    using var fileStream = File.Create(coverPath);
                    await imageStream.CopyToAsync(fileStream, token);
                    return true;
                }

                if (string.IsNullOrEmpty(mimeType))
                    return false;

                // For other formats, convert to JPEG using SkiaSharp (run in background thread)
                return await Task.Run(() => ConvertAndWriteToJpeg(imageStream, coverPath, jpegQuality), token);
            }
            catch (Exception)
            {
                // Silently fail and return false for any errors
                return false;
            }
        }

        /// <summary>
        /// Converts an image stream to JPEG format and writes it to the specified path using SkiaSharp.
        /// Supports conversion from PNG, WebP, GIF, BMP, TIFF, AVIF, HEIC and other formats supported by SkiaSharp.
        /// </summary>
        /// <param name="imageStream">The source image stream</param>
        /// <param name="outputPath">The output file path</param>
        /// <param name="jpegQuality">JPEG quality (0-100)</param>
        /// <returns>True if conversion and write succeeded, false otherwise</returns>
        private static bool ConvertAndWriteToJpeg(Stream imageStream, string outputPath, int jpegQuality)
        {
            try
            {
                // Read stream into byte array for SkiaSharp
                byte[] imageBytes;
                using (var memoryStream = new MemoryStream())
                {
                    imageStream.CopyTo(memoryStream);
                    imageBytes = memoryStream.ToArray();
                }

                // Create SKData from byte array
                using var skData = SKData.CreateCopy(imageBytes);
                using var skImage = SKImage.FromEncodedData(skData);

                if (skImage == null)
                {
                    return false;
                }

                // Encode as JPEG with specified quality
                using var encodedData = skImage.Encode(SKEncodedImageFormat.Jpeg, jpegQuality);

                if (encodedData == null)
                {
                    return false;
                }

                // Write to file
                using var fileStream = File.Create(outputPath);
                encodedData.SaveTo(fileStream);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        public static async Task<string?> AddExtensionImageAsync(this ThumbCacheService thumbs, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string url = "ext://" + path;
            return await thumbs.AddUrlAsync(url, null, token).ConfigureAwait(false);
        }
        public static async Task<string?> AddStorageImageAsync(this ThumbCacheService thumbs, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string url = "storage://" + path;
            return await thumbs.AddUrlAsync(url, null, token).ConfigureAwait(false);
        }
    }
}

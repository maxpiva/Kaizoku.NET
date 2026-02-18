using SkiaSharp;

namespace KaizokuBackend.Services.Archives;

/// <summary>
/// Archive writer for PDF format using SkiaSharp
/// </summary>
public class PdfArchiveWriter : IArchiveWriter
{
    private readonly Stream _outputStream;
    private readonly List<(string Name, byte[] Data)> _images = new();
    private bool _finalized;
    private bool _disposed;

    // Known image extensions
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".tiff", ".jxl", ".jp2", ".heic", ".heif"
    };

    public PdfArchiveWriter(Stream outputStream)
    {
        _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
    }

    /// <inheritdoc/>
    public string FileExtension => ".pdf";

    /// <inheritdoc/>
    public async Task WriteEntryAsync(string entryName, Stream content, CancellationToken token = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PdfArchiveWriter));
        if (_finalized) throw new InvalidOperationException("Archive has been finalized");
        if (content == null) throw new ArgumentNullException(nameof(content));

        // Skip non-image entries (like ComicInfo.xml)
        string extension = Path.GetExtension(entryName);
        if (!ImageExtensions.Contains(extension))
        {
            return;
        }

        // Read image data into memory
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, token).ConfigureAwait(false);
        _images.Add((entryName, ms.ToArray()));
    }

    /// <inheritdoc/>
    public Task FinalizeAsync(CancellationToken token = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PdfArchiveWriter));
        if (_finalized) return Task.CompletedTask;
        _finalized = true;

        // Sort images by name to maintain page order
        var sortedImages = _images.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (sortedImages.Count == 0)
        {
            throw new InvalidOperationException("Cannot create PDF with no images");
        }

        // Create PDF document
        using var document = SKDocument.CreatePdf(_outputStream);

        foreach (var (name, data) in sortedImages)
        {
            token.ThrowIfCancellationRequested();

            using var imageData = SKData.CreateCopy(data);
            using var bitmap = SKBitmap.Decode(imageData);

            if (bitmap == null)
            {
                // Skip images that can't be decoded
                continue;
            }

            // Create a page with the image's dimensions
            var pageSize = new SKSize(bitmap.Width, bitmap.Height);
            using var canvas = document.BeginPage(pageSize.Width, pageSize.Height);

            // Draw the image
            canvas.DrawBitmap(bitmap, 0, 0);

            document.EndPage();
        }

        document.Close();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _images.Clear();
        await _outputStream.DisposeAsync().ConfigureAwait(false);
    }
}

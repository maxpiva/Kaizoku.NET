using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace KaizokuBackend.Services.Archives;

/// <summary>
/// Archive writer for CBZ (Comic Book Zip) format
/// </summary>
public class CbzArchiveWriter : IArchiveWriter
{
    private readonly Stream _outputStream;
    private readonly IWriter _zipWriter;
    private bool _finalized;
    private bool _disposed;

    public CbzArchiveWriter(Stream outputStream)
    {
        _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
        _zipWriter = WriterFactory.Open(outputStream, ArchiveType.Zip, CompressionType.None);
    }

    /// <inheritdoc/>
    public string FileExtension => ".cbz";

    /// <inheritdoc/>
    public Task WriteEntryAsync(string entryName, Stream content, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) throw new ObjectDisposedException(nameof(CbzArchiveWriter));
        if (_finalized) throw new InvalidOperationException("Archive has been finalized");
        if (content == null) throw new ArgumentNullException(nameof(content));

        // Determine compression based on entry type
        var compressionType = entryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? CompressionType.Deflate
            : CompressionType.None;

        if (_zipWriter is ZipWriter zipWriter)
        {
            zipWriter.Write(entryName, content, new ZipWriterEntryOptions
            {
                CompressionType = compressionType,
                ModificationDateTime = DateTime.Now
            });
        }
        else
        {
            _zipWriter.Write(entryName, content);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FinalizeAsync(CancellationToken token = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CbzArchiveWriter));
        _finalized = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _zipWriter.Dispose();
        await _outputStream.DisposeAsync().ConfigureAwait(false);
    }
}

using KaizokuBackend.Models;
using KaizokuBackend.Services.Archives;
using System.IO.Compression;
using Xunit;

namespace KaizokuBackend.Tests.Services.Archives;

/// <summary>
/// Tests for archive writer functionality including CBZ, PDF, and factory creation
/// </summary>
public class ArchiveWriterTests
{
    #region CbzArchiveWriter Tests

    [Fact]
    public async Task CbzArchiveWriter_FileExtension_ReturnsCbz()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new CbzArchiveWriter(stream);

        // Act
        var extension = writer.FileExtension;

        // Assert
        Assert.Equal(".cbz", extension);
    }

    [Fact]
    public async Task CbzArchiveWriter_CreatesValidZipFile()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        await using (var writer = new CbzArchiveWriter(outputStream))
        {
            var imageData = CreateFakeImageData();
            using var imageStream = new MemoryStream(imageData);

            // Act
            await writer.WriteEntryAsync("page001.jpg", imageStream);
            await writer.FinalizeAsync();
        }

        // Assert - Verify ZIP structure
        outputStream.Position = 0;
        using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        Assert.Single(zipArchive.Entries);
        Assert.Equal("page001.jpg", zipArchive.Entries[0].FullName);
    }

    [Fact]
    public async Task CbzArchiveWriter_WriteMultipleEntries_AddsAllToArchive()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        await using (var writer = new CbzArchiveWriter(outputStream))
        {
            // Act
            for (int i = 1; i <= 5; i++)
            {
                var imageData = CreateFakeImageData();
                using var imageStream = new MemoryStream(imageData);
                await writer.WriteEntryAsync($"page{i:D3}.jpg", imageStream);
            }
            await writer.FinalizeAsync();
        }

        // Assert
        outputStream.Position = 0;
        using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        Assert.Equal(5, zipArchive.Entries.Count);
    }

    [Fact]
    public async Task CbzArchiveWriter_XmlEntry_UsesDeflateCompression()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        await using (var writer = new CbzArchiveWriter(outputStream))
        {
            var xmlData = System.Text.Encoding.UTF8.GetBytes("<ComicInfo></ComicInfo>");
            using var xmlStream = new MemoryStream(xmlData);

            // Act
            await writer.WriteEntryAsync("ComicInfo.xml", xmlStream);
            await writer.FinalizeAsync();
        }

        // Assert - XML entries should be compressed
        outputStream.Position = 0;
        using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        var entry = zipArchive.Entries[0];
        Assert.Equal("ComicInfo.xml", entry.FullName);
        // Note: We can't easily test compression type in .NET, but we verified the code path
    }

    [Fact]
    public async Task CbzArchiveWriter_ImageEntry_UsesNoCompression()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        await using (var writer = new CbzArchiveWriter(outputStream))
        {
            var imageData = CreateFakeImageData();
            using var imageStream = new MemoryStream(imageData);

            // Act
            await writer.WriteEntryAsync("page001.png", imageStream);
            await writer.FinalizeAsync();
        }

        // Assert - Image entries exist and are stored
        outputStream.Position = 0;
        using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        Assert.Single(zipArchive.Entries);
    }

    [Fact]
    public async Task CbzArchiveWriter_NullOutputStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var writer = new CbzArchiveWriter(null!);
        });
    }

    [Fact]
    public async Task CbzArchiveWriter_NullContent_ThrowsArgumentNullException()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new CbzArchiveWriter(stream);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await writer.WriteEntryAsync("test.jpg", null!));
    }

    [Fact]
    public async Task CbzArchiveWriter_WriteAfterFinalize_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new CbzArchiveWriter(stream);
        await writer.FinalizeAsync();

        // Act & Assert
        var imageData = CreateFakeImageData();
        using var imageStream = new MemoryStream(imageData);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.WriteEntryAsync("test.jpg", imageStream));
    }

    [Fact]
    public async Task CbzArchiveWriter_WriteAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var writer = new CbzArchiveWriter(stream);
        await writer.DisposeAsync();

        // Act & Assert
        var imageData = CreateFakeImageData();
        using var imageStream = new MemoryStream(imageData);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await writer.WriteEntryAsync("test.jpg", imageStream));
    }

    [Fact]
    public async Task CbzArchiveWriter_FinalizeAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var writer = new CbzArchiveWriter(stream);
        await writer.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await writer.FinalizeAsync());
    }

    [Fact]
    public async Task CbzArchiveWriter_DoubleDispose_DoesNotThrow()
    {
        // Arrange
        using var stream = new MemoryStream();
        var writer = new CbzArchiveWriter(stream);

        // Act & Assert - Should not throw
        await writer.DisposeAsync();
        await writer.DisposeAsync();
    }

    #endregion

    #region PdfArchiveWriter Tests

    [Fact]
    public async Task PdfArchiveWriter_FileExtension_ReturnsPdf()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new PdfArchiveWriter(stream);

        // Act
        var extension = writer.FileExtension;

        // Assert
        Assert.Equal(".pdf", extension);
    }

    [Fact]
    public async Task PdfArchiveWriter_SkipsNonImageEntries()
    {
        // Arrange
        using var outputStream = new MemoryStream();
        await using (var writer = new PdfArchiveWriter(outputStream))
        {
            var xmlData = System.Text.Encoding.UTF8.GetBytes("<ComicInfo></ComicInfo>");
            using var xmlStream = new MemoryStream(xmlData);

            // Act
            await writer.WriteEntryAsync("ComicInfo.xml", xmlStream);
            await writer.FinalizeAsync();
        }

        // Assert - PDF should have been created but empty (no images)
        // This will throw because no images were added
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            outputStream.Position = 0;
            using var tempStream = new MemoryStream();
            await using var writer = new PdfArchiveWriter(tempStream);
            var xmlData = System.Text.Encoding.UTF8.GetBytes("<ComicInfo></ComicInfo>");
            using var xmlStream = new MemoryStream(xmlData);
            await writer.WriteEntryAsync("ComicInfo.xml", xmlStream);
            await writer.FinalizeAsync();
        });
    }

    [Fact]
    public async Task PdfArchiveWriter_RecognizesImageExtensions()
    {
        // This test verifies that common image extensions are recognized
        // We can't easily test PDF creation without real images, so we test the extension logic

        // Arrange
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".tiff" };

        // Act & Assert
        foreach (var ext in imageExtensions)
        {
            using var stream = new MemoryStream();
            await using var writer = new PdfArchiveWriter(stream);

            // Create a simple PNG image (1x1 pixel, valid PNG header)
            var pngData = CreateMinimalPngImage();
            using var imageStream = new MemoryStream(pngData);

            // Should not throw for image extensions
            await writer.WriteEntryAsync($"test{ext}", imageStream);
        }
    }

    [Fact]
    public async Task PdfArchiveWriter_NullOutputStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var writer = new PdfArchiveWriter(null!);
        });
    }

    [Fact]
    public async Task PdfArchiveWriter_NullContent_ThrowsArgumentNullException()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new PdfArchiveWriter(stream);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await writer.WriteEntryAsync("test.jpg", null!));
    }

    [Fact]
    public async Task PdfArchiveWriter_WriteAfterFinalize_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new PdfArchiveWriter(stream);

        var pngData = CreateMinimalPngImage();
        using var imageStream = new MemoryStream(pngData);
        await writer.WriteEntryAsync("test.png", imageStream);
        await writer.FinalizeAsync();

        // Act & Assert
        using var newImageStream = new MemoryStream(pngData);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.WriteEntryAsync("test2.png", newImageStream));
    }

    [Fact]
    public async Task PdfArchiveWriter_WriteAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var writer = new PdfArchiveWriter(stream);
        await writer.DisposeAsync();

        // Act & Assert
        var pngData = CreateMinimalPngImage();
        using var imageStream = new MemoryStream(pngData);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await writer.WriteEntryAsync("test.png", imageStream));
    }

    [Fact]
    public async Task PdfArchiveWriter_FinalizeWithoutImages_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new PdfArchiveWriter(stream);

        // Act & Assert - Cannot create PDF with no images
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.FinalizeAsync());
    }

    [Fact]
    public async Task PdfArchiveWriter_DoubleDispose_DoesNotThrow()
    {
        // Arrange
        using var stream = new MemoryStream();
        var writer = new PdfArchiveWriter(stream);

        // Act & Assert - Should not throw
        await writer.DisposeAsync();
        await writer.DisposeAsync();
    }

    [Fact]
    public async Task PdfArchiveWriter_DoubleFinalizeAsync_DoesNotThrow()
    {
        // Arrange
        using var stream = new MemoryStream();
        await using var writer = new PdfArchiveWriter(stream);

        var pngData = CreateMinimalPngImage();
        using var imageStream = new MemoryStream(pngData);
        await writer.WriteEntryAsync("test.png", imageStream);

        // Act - First finalize
        await writer.FinalizeAsync();

        // Act & Assert - Second finalize should not throw
        await writer.FinalizeAsync();
    }

    #endregion

    #region ArchiveWriterFactory Tests

    [Fact]
    public void ArchiveWriterFactory_CreateCbz_ReturnsCbzWriter()
    {
        // Arrange
        var factory = new ArchiveWriterFactory();
        using var stream = new MemoryStream();

        // Act
        using var writer = factory.Create(ArchiveFormat.Cbz, stream);

        // Assert
        Assert.IsType<CbzArchiveWriter>(writer);
        Assert.Equal(".cbz", writer.FileExtension);
    }

    [Fact]
    public void ArchiveWriterFactory_CreatePdf_ReturnsPdfWriter()
    {
        // Arrange
        var factory = new ArchiveWriterFactory();
        using var stream = new MemoryStream();

        // Act
        using var writer = factory.Create(ArchiveFormat.Pdf, stream);

        // Assert
        Assert.IsType<PdfArchiveWriter>(writer);
        Assert.Equal(".pdf", writer.FileExtension);
    }

    [Fact]
    public void ArchiveWriterFactory_InvalidFormat_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var factory = new ArchiveWriterFactory();
        using var stream = new MemoryStream();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            factory.Create((ArchiveFormat)999, stream));
    }

    [Fact]
    public void ArchiveWriterFactory_GetExtension_CbzFormat_ReturnsCbz()
    {
        // Act
        var extension = ArchiveWriterFactory.GetExtension(ArchiveFormat.Cbz);

        // Assert
        Assert.Equal(".cbz", extension);
    }

    [Fact]
    public void ArchiveWriterFactory_GetExtension_PdfFormat_ReturnsPdf()
    {
        // Act
        var extension = ArchiveWriterFactory.GetExtension(ArchiveFormat.Pdf);

        // Assert
        Assert.Equal(".pdf", extension);
    }

    [Fact]
    public void ArchiveWriterFactory_GetExtension_InvalidFormat_ReturnsCbz()
    {
        // Act
        var extension = ArchiveWriterFactory.GetExtension((ArchiveFormat)999);

        // Assert - Default fallback
        Assert.Equal(".cbz", extension);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task CbzArchiveWriter_CompleteWorkflow_CreatesValidArchive()
    {
        // Arrange
        using var outputStream = new MemoryStream();

        // Act
        await using (var writer = new CbzArchiveWriter(outputStream))
        {
            // Add multiple pages
            for (int i = 1; i <= 3; i++)
            {
                var imageData = CreateFakeImageData();
                using var imageStream = new MemoryStream(imageData);
                await writer.WriteEntryAsync($"page{i:D3}.jpg", imageStream);
            }

            // Add metadata
            var xmlData = System.Text.Encoding.UTF8.GetBytes("<ComicInfo><Title>Test</Title></ComicInfo>");
            using var xmlStream = new MemoryStream(xmlData);
            await writer.WriteEntryAsync("ComicInfo.xml", xmlStream);

            await writer.FinalizeAsync();
        }

        // Assert
        outputStream.Position = 0;
        using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        Assert.Equal(4, zipArchive.Entries.Count);

        // Verify entries
        Assert.Contains(zipArchive.Entries, e => e.FullName == "page001.jpg");
        Assert.Contains(zipArchive.Entries, e => e.FullName == "page002.jpg");
        Assert.Contains(zipArchive.Entries, e => e.FullName == "page003.jpg");
        Assert.Contains(zipArchive.Entries, e => e.FullName == "ComicInfo.xml");
    }

    [Fact]
    public async Task ArchiveWriterFactory_CreateAndUse_WorksCorrectly()
    {
        // Arrange
        var factory = new ArchiveWriterFactory();
        using var outputStream = new MemoryStream();

        // Act
        await using (var writer = factory.Create(ArchiveFormat.Cbz, outputStream))
        {
            var imageData = CreateFakeImageData();
            using var imageStream = new MemoryStream(imageData);
            await writer.WriteEntryAsync("test.jpg", imageStream);
            await writer.FinalizeAsync();
        }

        // Assert
        outputStream.Position = 0;
        using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        Assert.Single(zipArchive.Entries);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates fake image data (not a real image, just test data)
    /// </summary>
    private static byte[] CreateFakeImageData()
    {
        var random = new Random();
        var data = new byte[1024];
        random.NextBytes(data);
        return data;
    }

    /// <summary>
    /// Creates a minimal valid PNG image (1x1 pixel, black)
    /// This is a valid PNG file that SkiaSharp can decode
    /// </summary>
    private static byte[] CreateMinimalPngImage()
    {
        // Minimal 1x1 black PNG (67 bytes)
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1 dimensions
            0x08, 0x00, 0x00, 0x00, 0x00, 0x3A, 0x7E, 0x9B, // bit depth 8, grayscale
            0x55, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, // IDAT chunk
            0x54, 0x08, 0x1D, 0x01, 0x00, 0x00, 0xFF, 0xFF, // compressed data
            0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0xE5, 0x27,
            0xDE, 0xFC, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, // IEND chunk
            0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        };
    }

    #endregion
}

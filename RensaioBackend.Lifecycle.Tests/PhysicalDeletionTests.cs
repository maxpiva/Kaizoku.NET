using RensaioBackend.Extensions;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

public class PhysicalDeletionTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "rensaio-delete-" + Guid.NewGuid());
    public PhysicalDeletionTests() => Directory.CreateDirectory(scratch);
    public void Dispose() => Directory.Delete(scratch, true);

    [Fact]
    public void ExplicitPhysicalDeletionRemovesArchivesSidecarsAndNestedFiles()
    {
        string seriesPath = Path.Combine(scratch, "Library", "Series");
        Directory.CreateDirectory(Path.Combine(seriesPath, "nested"));
        foreach (string file in new[] { "chapter.cbz", "rensaio.json", "cover.jpg", "nested/other.cbz" })
            File.WriteAllText(Path.Combine(seriesPath, file), "owned fixture");
        new SeriesEntity { StoragePath = "Series" }.DeletePhysicalSeries(
            new SettingsDto { StorageFolder = Path.Combine(scratch, "Library") }, null);
        Assert.False(Directory.Exists(seriesPath));
        Assert.True(Directory.Exists(Path.Combine(scratch, "Library")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData(".")]
    [InlineData("")]
    public void UnsafeRelativePathsAreRejected(string path)
    {
        Assert.Throws<IOException>(() => FileSystemExtensions.ResolveSafeSeriesPath(scratch, path));
    }

    [Fact]
    public void RootedPathsAreRejected()
    {
        Assert.Throws<IOException>(() => FileSystemExtensions.ResolveSafeSeriesPath(scratch, scratch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymlinkAncestorsAndNestedLinksAreRejectedBeforeDeletion(bool nested)
    {
        string outside = Path.Combine(scratch, "outside");
        string library = Path.Combine(scratch, "library");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(outside, "keep.cbz"), "keep");
        string series = Path.Combine(library, "Series");
        if (nested)
        {
            Directory.CreateDirectory(series);
            File.WriteAllText(Path.Combine(series, "first.cbz"), "keep too");
            Directory.CreateSymbolicLink(Path.Combine(series, "link"), outside);
        }
        else Directory.CreateSymbolicLink(series, outside);
        Assert.Throws<IOException>(() => new SeriesEntity { StoragePath = "Series" }
            .DeletePhysicalSeries(new SettingsDto { StorageFolder = library }, null));
        Assert.True(File.Exists(Path.Combine(outside, "keep.cbz")));
        if (nested) Assert.True(File.Exists(Path.Combine(series, "first.cbz")));
    }
}

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using RensaioBackend.Services.Helpers;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
async Task Reject(Func<Task> action, string message)
{
    try { await action(); } catch (InvalidDataException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
var scratch = Path.Combine(Path.GetTempPath(), "rensaio-cover-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var series = Path.Combine(scratch, "Series");
    Directory.CreateDirectory(series);
    Check(await SeriesCoverArchive.LoadAsync(scratch, "Series") == null, "missing cover skipped");
    var cover = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "cover.jpg"));
    await File.WriteAllBytesAsync(Path.Combine(series, "cover.jpg"), cover);
    var loaded = await SeriesCoverArchive.LoadAsync(scratch, "Series");
    Check(loaded != null && cover.SequenceEqual(loaded), "existing fixed JPEG loaded unchanged");
    await Reject(() => SeriesCoverArchive.LoadAsync(scratch, "../outside"), "path escape");
    await Reject(() => SeriesCoverArchive.LoadAsync(scratch, Path.GetTempPath()), "absolute escape");
    await File.WriteAllBytesAsync(Path.Combine(series, "cover.jpg"), Encoding.UTF8.GetBytes("<svg>not jpeg</svg>"));
    await Reject(() => SeriesCoverArchive.LoadAsync(scratch, "Series"), "SVG/HTML refused");
    await File.WriteAllBytesAsync(Path.Combine(series, "cover.jpg"), new byte[SeriesCoverArchive.MaxBytes + 1]);
    await Reject(() => SeriesCoverArchive.LoadAsync(scratch, "Series"), "byte limit before read");
    byte[] oversizedHeader = { 0xff, 0xd8, 0xff, 0xc0, 0, 8, 8, 0x20, 1, 0, 1, 1, 0xff, 0xd9 };
    await Reject(() => { SeriesCoverArchive.ValidateJpeg(oversizedHeader); return Task.CompletedTask; }, "dimension limit");
    await Reject(() => { SeriesCoverArchive.ValidateJpeg(cover[..^2]); return Task.CompletedTask; }, "truncated JPEG");
    await File.WriteAllBytesAsync(Path.Combine(series, "cover.jpg"), cover);
    if (!OperatingSystem.IsWindows())
    {
        Directory.CreateSymbolicLink(Path.Combine(scratch, "LinkedSeries"), series);
        await Reject(() => SeriesCoverArchive.LoadAsync(scratch, "LinkedSeries"), "directory symlink");
        File.Delete(Path.Combine(series, "cover.jpg"));
        File.CreateSymbolicLink(Path.Combine(series, "cover.jpg"), Path.Combine(AppContext.BaseDirectory, "cover.jpg"));
        await Reject(() => SeriesCoverArchive.LoadAsync(scratch, "Series"), "file symlink");
        File.Delete(Path.Combine(series, "cover.jpg"));
    }
    else Console.WriteLine("SKIP: symlink checks need supported platform permissions");

    using var original = new MemoryStream(Encoding.UTF8.GetBytes("<ComicInfo><Series>Series</Series><Number>12.5</Number><PageCount>2</PageCount><Notes>Created by Rensaiō</Notes></ComicInfo>"));
    using var withCover = SeriesCoverArchive.AddComicInfoCover(original, 3);
    using var archiveStream = new MemoryStream();
    await using (var writer = await WriterFactory.OpenAsyncWriter(archiveStream, ArchiveType.Zip, new ZipWriterOptions(CompressionType.None) { LeaveStreamOpen = true }))
    {
        using var coverStream = new MemoryStream(cover);
        await writer.WriteAsync(SeriesCoverArchive.EntryName, coverStream);
        for (int page = 1; page <= 2; page++)
        {
            using var image = new MemoryStream(cover);
            await writer.WriteAsync($"[Source][en] Series 12.5 {page}.jpg", image);
        }
        ((ZipWriter)writer).Write("ComicInfo.xml", withCover, new ZipWriterEntryOptions { CompressionType = CompressionType.Deflate });
    }
    archiveStream.Position = 0;
    using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
    var images = archive.Entries.Where(e => e.FullName.EndsWith(".jpg")).ToList();
    Check(images.Count == 3 && images[0].FullName == SeriesCoverArchive.EntryName, "ZIP insertion order and image count");
    Check(images.OrderBy(e => e.FullName, StringComparer.Ordinal).First().FullName == SeriesCoverArchive.EntryName, "natural name ordering cover first");
    using var comicInfo = archive.GetEntry("ComicInfo.xml")!.Open();
    var xml = XDocument.Load(comicInfo);
    Check(xml.Root!.Element("PageCount")!.Value == "3", "ComicInfo count includes cover");
    Check(xml.Root.Element("Number")!.Value == "12.5" && xml.Root.Element("Series")!.Value == "Series", "fraction and series metadata preserved");
    var frontCover = xml.Root.Element("Pages")!.Element("Page")!;
    Check(frontCover.Attribute("Image")!.Value == "0" && frontCover.Attribute("Type")!.Value == "FrontCover", "ComicInfo references image zero");
    Check(archive.Entries.Count == 4, "archive integrity including ComicInfo");
    Console.WriteLine($"PASS: {checks} series-cover archive regression checks");
}
finally { Directory.Delete(scratch, true); }

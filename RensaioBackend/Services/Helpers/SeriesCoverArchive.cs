using System.Xml.Linq;

namespace RensaioBackend.Services.Helpers;

/// <summary>Reuses only the fixed, already materialized series JPEG; never fetches a URL.</summary>
public static class SeriesCoverArchive
{
    public const string EntryName = "0000-cover.jpg";
    public const int MaxBytes = 5 * 1024 * 1024;

    public static async Task<byte[]?> LoadAsync(string storageRoot, string seriesPath, CancellationToken token = default)
    {
        var root = Path.GetFullPath(storageRoot);
        var folder = Path.GetFullPath(Path.Combine(root, seriesPath));
        var relative = Path.GetRelativePath(root, folder);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidDataException("Series cover is outside the configured storage folder.");
        // Reject symlink/reparse-point hops below the configured root, including the fixed leaf.
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Series cover cannot traverse symbolic links.");
        }
        var path = Path.Combine(folder, "cover.jpg");
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Series cover cannot be a symbolic link.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (file.Length <= 0 || file.Length > MaxBytes) throw new InvalidDataException("Series cover must be a JPEG of at most 5 MiB.");
        var data = new byte[(int)file.Length];
        await file.ReadExactlyAsync(data, token).ConfigureAwait(false);
        ValidateJpeg(data);
        return data;
    }

    // Bound dimensions before passing bytes to the normal decoder; refuse SVG/HTML or a
    // renamed PNG rather than trusting a filename or content type.
    public static void ValidateJpeg(byte[] data)
    {
        if (data.Length < 4 || data.Length > MaxBytes || data[0] != 0xff || data[1] != 0xd8 ||
            data[^2] != 0xff || data[^1] != 0xd9) throw new InvalidDataException("Invalid series-cover JPEG.");
        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset++] != 0xff) break;
            while (offset < data.Length && data[offset] == 0xff) offset++;
            if (offset >= data.Length) break;
            var marker = data[offset++];
            if (marker == 0xda || marker == 0xd9) break;
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            if (offset + 2 > data.Length) break;
            var length = (data[offset] << 8) | data[offset + 1];
            if (length < 2 || offset + length > data.Length) break;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) break;
                var height = (data[offset + 3] << 8) | data[offset + 4];
                var width = (data[offset + 5] << 8) | data[offset + 6];
                if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 32_000_000)
                    throw new InvalidDataException("Series cover dimensions exceed the supported limit (8192 per side, 32 megapixels).");
                return;
            }
            offset += length;
        }
        throw new InvalidDataException("Unsupported or malformed series-cover JPEG header.");
    }

    /// <summary>Image 0 is the injected cover, in ZIP insertion and natural filename order.</summary>
    public static MemoryStream AddComicInfoCover(Stream original, int pageCount)
    {
        if (pageCount < 2) throw new ArgumentOutOfRangeException(nameof(pageCount));
        // The input is generated locally by CreateComicInfo, not an external XML document.
        var xml = XDocument.Load(original);
        var root = xml.Root ?? throw new InvalidDataException("Missing ComicInfo root.");
        root.SetElementValue("PageCount", pageCount);
        root.Element("Pages")?.Remove();
        root.Add(new XElement("Pages", new XElement("Page", new XAttribute("Image", 0), new XAttribute("Type", "FrontCover"))));
        var result = new MemoryStream();
        xml.Save(result);
        result.Position = 0;
        return result;
    }
}

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Helpers;
using SharpCompress.Archives;

namespace RensaioBackend.Services.Series;

public partial class SeriesArchiveService
{
    // This is an explicit action only. Changing IsStorage must never call this method.
    public async Task<SourceDuplicateCleanupResultDto> CleanupSourceDuplicatesAsync(
        Guid seriesId, Guid sourceId, CancellationToken token = default)
    {
        using var mutation = await SeriesMutationLock.AcquireAsync(seriesId, token).ConfigureAwait(false);
        var series = await _db.Series.Include(s => s.Sources)
            .SingleOrDefaultAsync(s => s.Id == seriesId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Series not found.");
        // A scoped context may have loaded sources before it waited for the gate.
        // Destructive decisions must use persisted state, not tracked stale flags/chapters.
        await _db.Entry(series).ReloadAsync(token).ConfigureAwait(false);
        if (_db.Entry(series).State == EntityState.Detached)
            throw new KeyNotFoundException("Series not found.");
        foreach (var source in series.Sources.ToList())
        {
            await _db.Entry(source).ReloadAsync(token).ConfigureAwait(false);
            if (_db.Entry(source).State == EntityState.Detached || source.SeriesId != seriesId)
                series.Sources.Remove(source);
        }
        var target = series.Sources.SingleOrDefault(s => s.Id == sourceId && s.SeriesId == seriesId)
            ?? throw new KeyNotFoundException("Source does not belong to this series.");
        if (target.IsStorage)
            throw new ArgumentException("Only temporary sources can be cleaned up.");

        var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        var folder = SafeExistingPath(settings.StorageFolder, series.StoragePath, true)
            ?? throw new ArgumentException("Series storage path is missing or unsafe.");
        // A folder shared by two series is not safe to mutate using one series' manifest.
        if (await _db.Series.AnyAsync(s => s.Id != seriesId && s.StoragePath == series.StoragePath, token))
            throw new ArgumentException("Series storage folder is shared.");

        var result = new SourceDuplicateCleanupResultDto();
        foreach (var chapter in target.Chapters.Where(c => !string.IsNullOrWhiteSpace(c.Filename)).ToList())
        {
            token.ThrowIfCancellationRequested();
            var filename = chapter.Filename!;
            var path = SafeArchivePath(folder, filename);
            // A file referenced by another tracked chapter is never ours alone to delete.
            if (path == null || !chapter.Number.HasValue || chapter.IsDeleted ||
                series.Sources.SelectMany(s => s.Chapters).Count(c =>
                    string.Equals(c.Filename, filename, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                result.Skipped++;
                continue;
            }

            string? recoveryPath = null;
            var oldPages = chapter.Pages;
            var oldPageCount = chapter.PageCount;
            var oldDownloadDate = chapter.DownloadDate;
            try
            {
                var identity = ReadHealthyArchive(path, chapter, target.Language, token);
                if (identity == null || !string.Equals(identity.Series, series.Title.Trim(), StringComparison.Ordinal))
                { result.Skipped++; continue; }
                bool backedUp = false;
                string? backingPath = null;
                foreach (var source in series.Sources.Where(s => s.IsStorage && !s.IsDisabled && !s.IsUninstalled &&
                    !string.IsNullOrWhiteSpace(s.Language) && string.Equals(s.Language, target.Language, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var permanent in source.Chapters.Where(c => !c.IsDeleted && c.Number == chapter.Number))
                    {
                        var permanentPath = SafeArchivePath(folder, permanent.Filename);
                        if (permanentPath == null || string.Equals(path, permanentPath, StringComparison.OrdinalIgnoreCase)) continue;
                        var permanentIdentity = ReadHealthyArchive(permanentPath, permanent, source.Language, token);
                        if (identity == permanentIdentity)
                        {
                            backedUp = true;
                            backingPath = permanentPath;
                            break;
                        }
                    }
                    if (backedUp) break;
                }
                if (!backedUp) { result.Skipped++; continue; }

                // Recheck containment immediately before mutation. A unique non-archive recovery
                // file makes DB-save failures recoverable without deleting the last tracked copy.
                if (SafeArchivePath(folder, filename) != path ||
                    SafeArchivePath(folder, Path.GetFileName(backingPath!)) != backingPath)
                { result.Skipped++; continue; }
                recoveryPath = path + ".cleanup-" + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Move(path, recoveryPath); }
                catch { recoveryPath = null; throw; }
                chapter.Filename = null;
                chapter.Pages = [];
                chapter.PageCount = null;
                chapter.DownloadDate = null;
                // Not manually deleted: a fallback remains eligible if the permanent copy vanishes.
                _db.Touch(target, s => s.Chapters);
                try
                {
                    // Finish the durable state transition even if the client disconnects after rename.
                    await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    File.Move(recoveryPath, path);
                    recoveryPath = null;
                    chapter.Filename = filename;
                    chapter.Pages = oldPages;
                    chapter.PageCount = oldPageCount;
                    chapter.DownloadDate = oldDownloadDate;
                    _db.Entry(target).State = EntityState.Unchanged;
                    throw;
                }
                result.Deleted++;
                try { File.Delete(recoveryPath); recoveryPath = null; }
                catch (IOException ex) { _logger.LogWarning(ex, "Duplicate removed from library but recovery file remains at {Path}", recoveryPath); }
                catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Duplicate removed from library but recovery file remains at {Path}", recoveryPath); }
                try { _hashCache.DeleteChapterHash(series.StoragePath, filename); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to invalidate duplicate archive hash {Filename}", filename); }
            }
            catch (IOException ex) when (recoveryPath == null)
            {
                _logger.LogWarning(ex, "Skipped inaccessible duplicate archive {Filename}", filename);
                result.Skipped++;
            }
            catch (UnauthorizedAccessException ex) when (recoveryPath == null)
            {
                _logger.LogWarning(ex, "Skipped inaccessible duplicate archive {Filename}", filename);
                result.Skipped++;
            }
        }
        if (result.Deleted > 0)
        {
            try { await _stateService.SyncToRensaioJsonAsync(seriesId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to sync rensaio.json after source duplicate cleanup {SeriesId}", seriesId); }
        }
        return result;
    }

    private static string? SafeArchivePath(string folder, string? filename)
    {
        // Only directly tracked CBZ files; nested, rooted and non-ZIP archives are skipped.
        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename ||
            filename.Contains('\\') || !filename.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase)) return null;
        return SafeExistingPath(folder, filename, false);
    }

    private static string? SafeExistingPath(string root, string relative, bool directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                relative.Split('/', '\\').Any(segment => segment is "." or "..")) return null;
            var basePath = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(basePath, relative));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)) return null;
            // Check every ancestor, including the configured storage root, for links/reparse points.
            for (var current = fullPath; current != null; current = Path.GetDirectoryName(current))
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
            }
            return (directory ? Directory.Exists(fullPath) : File.Exists(fullPath)) ? fullPath : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    private sealed record ArchiveIdentity(string Series, string Number, string Language, string Title, string Volume, string Format, string Edition);

    private static ArchiveIdentity? ReadHealthyArchive(string path, Chapter chapter, string language, CancellationToken token)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.ReadByte() != 'P' || stream.ReadByte() != 'K') return null;
            stream.Position = 0;
            using var archive = ArchiveFactory.OpenArchive(stream);
            XDocument? info = null;
            int images = 0;
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsEncrypted || entry.Size <= 0) return null;
                using var content = entry.OpenEntryStream();
                uint crc = uint.MaxValue;
                long length = 0;
                var buffer = new byte[81920];
                bool isInfo = string.Equals(entry.Key, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase);
                using var xml = isInfo ? new MemoryStream() : null;
                int read;
                while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    length += read;
                    if (length > entry.Size || (isInfo && length > 1024 * 1024)) return null;
                    for (int i = 0; i < read; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                    xml?.Write(buffer, 0, read);
                }
                if (length != entry.Size || (crc ^ uint.MaxValue) != unchecked((uint)entry.Crc)) return null;
                if (ArchiveHelperService.ArchiveIsImage(entry.Key ?? "")) images++;
                if (xml != null)
                {
                    if (info != null) return null;
                    xml.Position = 0;
                    using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    info = XDocument.Load(reader);
                }
            }
            if (images == 0 || info?.Root?.Name != "ComicInfo" ||
                info.Root.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1) ||
                (chapter.PageCount.HasValue && chapter.PageCount.Value != images)) return null;
            string Field(string name) => info.Root.Element(name)?.Value.Trim() ?? "";
            if (!decimal.TryParse(Field("Number"), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number != chapter.Number ||
                string.IsNullOrWhiteSpace(language) || !string.Equals(Field("LanguageISO"), language, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(Field("Series")) || string.IsNullOrWhiteSpace(Field("Title"))) return null;
            if (int.TryParse(Field("PageCount"), out var count) && count != images) return null;
            return new ArchiveIdentity(Field("Series"), number.ToString(CultureInfo.InvariantCulture), language.ToLowerInvariant(),
                Field("Title"), Field("Volume"), Field("Format"), Field("Edition"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        uint crc = (uint)value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();
}

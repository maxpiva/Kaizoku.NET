using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Import.KavitaParser;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using static System.Net.Mime.MediaTypeNames;

namespace KaizokuBackend.Services.Helpers
{
    public class ArchiveHelperService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ArchiveHelperService> _logger;
        private readonly SettingsService _settings;
        private readonly SuwayomiClient _suwayomi;
        public ArchiveHelperService(AppDbContext db, ILogger<ArchiveHelperService> logger, SuwayomiClient suwayomi,  SettingsService settings)
        {
            _db = db;
            _logger = logger;
            _settings = settings;
            _suwayomi = suwayomi;
        }
        public async Task WriteComicThumbnailAsync(Models.Database.Series series, CancellationToken token = default)
        {
            Models.Settings settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(series.ThumbnailUrl) || series.ThumbnailUrl.Contains("unknown"))
                return;
            string? lastPart = series.ThumbnailUrl.Split('/').LastOrDefault();
            if (string.IsNullOrEmpty(lastPart))
                return;
            string fid = lastPart.Split('!').FirstOrDefault() ?? lastPart;
            if (int.TryParse(fid, out int rfid))
            {
                Stream ms = await _suwayomi.GetMangaThumbnailAsync(rfid, token).ConfigureAwait(false);
                if (ms.Length == 0)
                    return;
                await ms.WriteCoverJpegAsync(Path.Combine(settings.StorageFolder, series.StoragePath), 85, token);
            }
        }


        public async Task UpdateTitleAndAddComicInfoAsync(Guid seriesId, bool onlyDownloadByKaizoku = true,
         CancellationToken token = default)
        {
            Models.Database.Series? series = await _db.Series
                .Include(s => s.Sources).AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == seriesId, token).ConfigureAwait(false);
            if (series == null)
                return;
            await UpdateTitleAndAddComicInfoAsync(series, onlyDownloadByKaizoku, token).ConfigureAwait(false);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        public async Task UpdateTitleAndAddComicInfoAsync(Models.Database.Series series, bool onlyDownloadByKaizoku = true, CancellationToken token = default)
        {
            Models.Settings settings = await _settings.GetSettingsAsync(token);
            foreach (var sp2 in series.Sources)
            {
                var sp = await _db.SeriesProviders
                    .FirstOrDefaultAsync(s => s.Id == sp2.Id, token).ConfigureAwait(false);
                if (sp.Chapters == null)
                    continue;
                foreach (var chap in sp.Chapters)
                {
                    if (string.IsNullOrEmpty(chap.Filename))
                        continue;
                    string archivePath = Path.Combine(settings.StorageFolder, series.StoragePath, chap.Filename);
                    if (!File.Exists(archivePath))
                        continue;
                    // Now check if the filename should be changed
                    string prefix = $"[{sp.Provider}][{sp.Language}]";
                    string safeName = MakeFileNameSafe(sp.Provider, sp.Scanlator, sp.Title, sp.Language, chap.Number, chap.Name, sp.ChapterCount);
                    string extension = Path.GetExtension(chap.Filename) ?? ".cbz";
                    string newFileName = safeName + extension;
                    if (!chap.Filename.StartsWith(prefix))
                    {
                        //Not ours or renamed by us
                        continue;
                    }
                    if (!onlyDownloadByKaizoku)
                        extension = ".cbz"; // Force .cbz, we don't write other formats.
                    if (!string.Equals(newFileName, chap.Filename, StringComparison.OrdinalIgnoreCase))
                    {
                        string basePath = Path.Combine(settings.StorageFolder, series.StoragePath);
                        string oldFullPath = Path.Combine(basePath, chap.Filename);
                        string newFullPath = Path.Combine(basePath, newFileName);
                        try
                        {
                            //SharpCompress do not care if we call a rar a zip, so no issues to rename it first.
                            File.Move(oldFullPath, newFullPath);
                            chap.Filename = newFileName;
                            _db.Touch(sp, a => a.Chapters);
                            await _db.SaveChangesAsync(token).ConfigureAwait(false);
                            _logger.LogInformation("Renamed archive from {oldFullPath} to {newFullPath} and updated chapters filename.", oldFullPath, newFullPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to rename archive from {oldFullPath} to {newFullPath}", oldFullPath, newFullPath);
                        }

                        archivePath = newFullPath;
                    }

                    if (onlyDownloadByKaizoku)
                    {
                        if (!chap.Filename.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    var fileNames = GetArchiveFileNames(archivePath);
                    var comicInfoName = fileNames.FirstOrDefault(f =>
                        f.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                    var imageFiles = fileNames.Where(f => ArchiveIsImage(f)).ToList();
                    if (imageFiles.Count == 0)
                        continue;
                    if (onlyDownloadByKaizoku)
                    {
                        // Must contain ComicInfo.xml (case-insensitive)
                        if (comicInfoName == null)
                            continue;
                        // All image files must start with [Provider][Language]
                        if (!imageFiles.All(f =>
                                Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                    // All criteria matched, generate new ComicInfo.xml
                    ComicInfo newComicInfo = CreateComicInfo(series, sp, chap, imageFiles.Count);
                    if (comicInfoName != null)
                    {
                        ComicInfo? oldComicInfo = ArchiveHelperService.GetArchiveFile(archivePath, comicInfoName)
                            ?.ToComicInfo();
                        if (oldComicInfo != null)
                        {
                            if (oldComicInfo.Notes != null && oldComicInfo.Notes.Contains("Kaizoku"))
                            {
                                if (oldComicInfo.Series == newComicInfo.Series && oldComicInfo.LocalizedSeries == newComicInfo.LocalizedSeries)
                                {
                                    //Already fixed.
                                    continue;
                                }
                            }
                        }
                    }
                    // Replace ComicInfo.xml in archive
                    try
                    {
                        _logger.LogInformation("Updating ComicInfo.xml in {archivePath}", archivePath);
                        UpdateOrAddArchiveFile(archivePath, comicInfoName, newComicInfo.ToStream());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update ComicInfo.xml in {archivePath}", archivePath);
                    }
                }
            }

            string path = Path.Combine(settings.StorageFolder, series.StoragePath);
            _logger.LogInformation("Writing Cover.jpg in {archivePath}", path);
            await WriteComicThumbnailAsync(series, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Recursively processes all series and chapters, and fixes ComicInfo.xml in .cbz archives that match criteria.
        /// </summary>
        public async Task UpdateAllTitlesAndAddComicInfoAsync(ProgressReporter reporter, bool onlyDownloadByKaizoku = true, CancellationToken token = default)
        {
            reporter.Report(ProgressStatus.Started,0, "Updating all Series...");
            var allSeries = await _db.Series.Include(s => s.Sources).AsNoTracking().ToListAsync(token);
            float step = 100 / (float)allSeries.Count;
            float acum = 0;
            foreach (var series in allSeries)
            {
                reporter.Report(ProgressStatus.InProgress, (decimal)acum, $"Updating {series.Title}...");
                await UpdateTitleAndAddComicInfoAsync(series, onlyDownloadByKaizoku, token);
                acum += step;
            }
            reporter.Report(ProgressStatus.Completed, (decimal)acum, $"Update Complete.");
        }

        /// <summary>
        /// Gets a list of file names from an archive without knowing the archive type
        /// </summary>
        /// <param name="archivePath">Path to the archive file</param>
        /// <returns>List of file names in the archive, or empty list if archive is not supported</returns>
        public static List<string> GetArchiveFileNames(string archivePath)
        {
            var fileNames = new List<string>();

            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
            {
                return fileNames;
            }

            try
            {
                // Try to open with IArchive first (supports most formats)
                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(stream);

                foreach (var entry in archive.Entries)
                {
                    if (entry.Key == null)
                        continue;
                    if (!entry.IsDirectory)
                    {
                        fileNames.Add(entry.Key);
                    }
                }
            }
            catch
            {
                // If ArchiveFactory fails, try with IReader (supports formats like TAR)
                try
                {
                    using var stream = File.OpenRead(archivePath);
                    using var reader = ReaderFactory.Open(stream);

                    while (reader.MoveToNextEntry())
                    {
                        if (reader.Entry.Key == null)
                            continue;
                        if (!reader.Entry.IsDirectory)
                        {
                            fileNames.Add(reader.Entry.Key);
                        }
                    }
                }
                catch
                {
                    // If both methods fail, return empty list
                    return new List<string>();
                }
            }

            return fileNames;
        }


        public static Stream? GetArchiveFile(string archivePath, string file)
        {
            try
            {
                // Try to open with IArchive first (supports most formats)
                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(stream);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        if (entry.Key == file)
                        {
                            MemoryStream ms = new MemoryStream();
                            entry.WriteTo(ms);
                            ms.Position = 0;
                            return ms;
                        }
                    }
                }

                return null;
            }
            catch
            {
                // If ArchiveFactory fails, try with IReader (supports formats like TAR)
                try
                {
                    using var stream = File.OpenRead(archivePath);
                    using var reader = ReaderFactory.Open(stream);

                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory)
                        {
                            if (reader.Entry.Key == file)
                            {
                                MemoryStream ms = new MemoryStream();
                                reader.WriteEntryTo(ms);
                                ms.Position = 0;
                                return ms;
                            }
                        }
                    }
                }
                catch
                {
                    // If both methods fail, return empty list
                    
                }
            }

            return null;

        }
        public static ArchiveResult CheckArchive(string archivePath)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                return ArchiveResult.NotFound;
            var fileNames = GetArchiveFileNames(archivePath);
            if (fileNames.Count == 0)
                return ArchiveResult.NotAnArchive;
            if (ArchiveItContainsImages(fileNames))
                return ArchiveResult.Fine;
            return ArchiveResult.NoImages;
        }

        private static readonly string[] imageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".tiff", ".jxl", ".jp2", ".heic",".heif"
        };
        public static bool ArchiveItContainsImages(List<string> fileNames)
        {
            return fileNames.Any(f =>
                !string.IsNullOrEmpty(f) &&
                imageExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            );
        }

        public static bool ArchiveIsImage(string filename)
        {
            return imageExtensions.Any(ext => filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }
        public static bool IsTitleChap(string str)
        {
            str = str.ToLowerInvariant();
            if (str.Contains("chapter") || str.Contains("chap") || str.Contains("ch."))
                return true;
            return false;
        }

        public static string MakeFileNameSafe(string provider, string? scanlator, string title, string language, decimal? chapter, string? chapterTitle = null, decimal? maxchap = null, int? page = null, int? maxpage = null)
        {
            provider = provider.Replace("-", "_");
            if (scanlator != null && provider != scanlator)
                provider += "-" + scanlator;
            provider = provider.Replace("[", "(").Replace("]", ")");

            string pageStr = "";
            string chapterStr = chapter.HasValue ? chapter.Value.FormatDecimal() : "";
            if (chapter.HasValue && maxchap.HasValue)
            {
                int length = ((int)maxchap.Value).ToString().Length;
                chapterStr = chapterStr.PadLeft(length, '0');
            }
            string lan = !string.IsNullOrEmpty(language) ? "[" + language.ToLowerInvariant() + "]" : "";
            string chapTit = !string.IsNullOrEmpty(chapterTitle) ? " (" + chapterTitle.Trim().Replace('(', '[').Replace(")", "]") + ")" : "";
            if (IsTitleChap(chapTit))
                chapTit = "";
            if (page.HasValue && maxpage.HasValue)
            {
                int length = maxpage.Value.ToString().Length;
                pageStr = " " + page.Value.ToString().PadLeft(length, '0');
            }
            title = title.Replace("(", "").Replace(")", "");
            string fullname = $"[{provider}]{lan} {title}{chapTit} {chapterStr}{pageStr}";

            string safeName = fullname.ReplaceInvalidFilenameAndPathCharacters();
            safeName = Regex.Replace(safeName, @"\s+", " ").Trim();
            return safeName;
        }

        public static ComicInfo CreateComicInfo(Models.Database.Series s, SeriesProvider sp, Chapter chap, int cnt)
        {
            List<string> ratings = Enum.GetNames<AgeRating>().ToList();
            ComicInfo info = new ComicInfo();
            if (sp.ChapterCount.HasValue)
                info.Count = (int)sp.ChapterCount.Value;
            string chapName = chap.Name?.Trim() ?? "";
            if (string.IsNullOrEmpty(chapName))
                chapName = "Chapter " + chap.Number?.FormatDecimal() ?? "";
            info.Title = chapName;
            info.Format = "Web";
            if (s.Genre.Count > 0)
            {
                info.Tags = string.Join(",", s.Genre);
                string? rating = s.Genre.FirstOrDefault(t => ratings.Contains(t, StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(rating))
                {
                    info.AgeRating = rating;
                }
            }
            info.LanguageISO = sp.Language.ToLowerInvariant();
            info.Number = chap.Number?.FormatDecimal() ?? "";
            info.PageCount = cnt;
            info.Series = sp.Title.Trim();
            info.LocalizedSeries = s.Title.Trim();
            info.Web = chap.Url!;
            info.Writer = sp.Author?.Trim() ?? s.Author?.Trim() ?? "";
            info.Publisher = sp.Provider;
            info.Translator = sp.Scanlator ?? "";
            info.CoverArtist = sp.Artist?.Trim() ?? s.Artist ?? "";
            if (chap.ProviderUploadDate.HasValue)
            {
                info.Day = chap.ProviderUploadDate.Value.Day;
                info.Month = chap.ProviderUploadDate.Value.Month;
                info.Year = chap.ProviderUploadDate.Value.Year;
            }

            string type = s.Type?.Trim().ToLowerInvariant() ?? "";
            if (type == "manga" || s.Genre.Contains("manga", StringComparer.InvariantCultureIgnoreCase))
            {
                info.Manga = "YesAndRightToLeft";
            }
            info.Notes = "Created by Kaizoku.NET";
            return info;
        }

        public static ComicInfo CreateComicInfo(ChapterDownload chDownload, int cnt)
        {
            List<string> ratings = Enum.GetNames<AgeRating>().ToList();
            ComicInfo info = new ComicInfo();
            if (chDownload.ChapterCount.HasValue)
                info.Count = (int)chDownload.ChapterCount.Value;
            string chapName = chDownload.ChapterName?.Trim() ?? "";
            if (string.IsNullOrEmpty(chapName))
                chapName = "Chapter " + chDownload.Chapter.ChapterNumber?.FormatDecimal() ?? "";
            info.Title = chapName;
            info.Format = "Web";
            if (chDownload.Tags.Count > 0)
            {
                info.Tags = string.Join(",", chDownload.Tags);
                string? rating = chDownload.Tags.FirstOrDefault(t => ratings.Contains(t, StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(rating))
                {
                    info.AgeRating = rating;
                }
            }
            info.LanguageISO = chDownload.Language.ToLowerInvariant();
            info.Number = chDownload.Chapter.ChapterNumber?.FormatDecimal() ?? "";
            info.PageCount = cnt;
            info.Series = chDownload.Title.Trim();
            info.LocalizedSeries = chDownload.SeriesTitle.Trim();
            info.Web = chDownload.Chapter.RealUrl;
            info.Writer = chDownload.Author?.Trim() ?? "";
            info.Publisher = chDownload.ProviderName;
            info.Translator = chDownload.Scanlator ?? "";
            info.CoverArtist = chDownload.Artist?.Trim() ?? "";
            if (chDownload.ComicUploadDateUTC.HasValue)
            {
                info.Day = chDownload.ComicUploadDateUTC.Value.Day;
                info.Month = chDownload.ComicUploadDateUTC.Value.Month;
                info.Year = chDownload.ComicUploadDateUTC.Value.Year;
            }

            string type = chDownload.Type?.Trim().ToLowerInvariant() ?? "";
            if (type == "manga" || chDownload.Tags.Contains("manga", StringComparer.InvariantCultureIgnoreCase))
            {
                info.Manga = "YesAndRightToLeft";
            }
            info.Notes = "Created by Kaizoku.NET";
            return info;
        }

        public static void UpdateOrAddArchiveFile(string archivePath, string? file, Stream contentStream)
        {
            if (string.IsNullOrEmpty(archivePath) || contentStream == null)
                throw new ArgumentException("Invalid arguments for UpdateOrAddArchiveFile");
            if (!File.Exists(archivePath))
                throw new FileNotFoundException($"Archive not found: {archivePath}");

            // Use a temp file for the new archive
            string tempArchivePath = Path.GetTempFileName();
            try
            {
                // Open the original archive
                using var originalStream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(originalStream);
                // Create a new archive (zip for simplicity, can be extended for other formats)
                using (var tempStream = File.Open(tempArchivePath, FileMode.Create, FileAccess.ReadWrite))
                using (var zipWriter = WriterFactory.Open(tempStream, ArchiveType.Zip, new WriterOptions(CompressionType.None)))
                {
                    // Copy all entries except the one to be replaced
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Key==null)
                            continue;
                        if (entry.IsDirectory)
                            continue;
                        if (file != null)
                        {
                            if (entry.Key.Replace("\\", "/")
                                .Equals(file.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase))
                                continue; // skip the file to be replaced
                        }

                        using var entryStream = entry.OpenEntryStream();
                        zipWriter.Write(entry.Key, entryStream, entry.LastModifiedTime);
                    }
                    // Add the new/updated file
                    if (contentStream.CanSeek)
                        contentStream.Position = 0;
                    ((ZipWriter)zipWriter).Write(file ?? "ComicInfo.xml", contentStream, new ZipWriterEntryOptions { CompressionType = CompressionType.Deflate, ModificationDateTime = DateTime.Now });
                }
                // Replace the original archive
                originalStream.Close();
                File.Copy(tempArchivePath, archivePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempArchivePath))
                {
                    try { File.Delete(tempArchivePath); } catch { }
                }
            }
        }

        /// <summary>
        /// Recursively checks if a directory contains any archive files with minimal filesystem impact.
        /// Returns true as soon as the first archive file is found to minimize I/O operations.
        /// </summary>
        /// <param name="directoryPath">The directory path to check</param>
        /// <returns>True if any archive files are found, false otherwise</returns>
        public static bool ContainsArchiveFilesRecursive(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return false;

            try
            {
                return ContainsArchiveFilesRecursiveInternal(directoryPath);
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted during scanning
                return false;
            }
            catch (Exception)
            {
                // Handle any other filesystem exceptions gracefully
                return false;
            }
        }

        /// <summary>
        /// Internal recursive implementation that checks for archive files.
        /// Uses early return strategy to minimize filesystem access.
        /// </summary>
        /// <param name="directoryPath">The directory path to check</param>
        /// <returns>True if any archive files are found, false otherwise</returns>
        private static bool ContainsArchiveFilesRecursiveInternal(string directoryPath)
        {
            try
            {
                // First check files in current directory for archive extensions
                // Use enumerator pattern to avoid loading all files into memory
                foreach (var file in Directory.EnumerateFiles(directoryPath))
                {
                    if (IsArchiveFile(file))
                    {
                        return true; // Early return on first match
                    }
                }

                // Then check subdirectories recursively
                // Use enumerator pattern to avoid loading all directories into memory
                foreach (var subDirectory in Directory.EnumerateDirectories(directoryPath))
                {
                    
                    try
                    {
                        if (ContainsArchiveFilesRecursiveInternal(subDirectory))
                        {
                            return true; // Early return on first match in subdirectory
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we don't have access to
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Directory was deleted during scanning
                        continue;
                    }
                }

                return false; // No archive files found in this directory tree
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted during scanning
                return false;
            }
        }

        /// <summary>
        /// Checks if a file is an archive based on the ArchiveFileExtensions pattern.
        /// Uses the same extensions as defined in Parser.ArchiveFileExtensions.
        /// </summary>
        /// <param name="filePath">The file path to check</param>
        /// <returns>True if the file is an archive, false otherwise</returns>
        public static bool IsArchiveFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            return Parser.IsArchive(filePath);
        }
    }
}

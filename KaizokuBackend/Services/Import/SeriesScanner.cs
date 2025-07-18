using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import.KavitaParser;
using KaizokuBackend.Services.Import.Models;
using KaizokuBackend.Services.Jobs.Report;
using Parser = KaizokuBackend.Services.Import.KavitaParser.Parser;
using SeriesInfo = KaizokuBackend.Services.Import.KavitaParser.SeriesInfo;

namespace KaizokuBackend.Services.Import
{
    public class SeriesScanner
    {
        private readonly BasicParser _parser;
        private readonly ILogger _logger;
        private readonly ContextProvider _baseUrl;
        private static readonly Regex KaizokuRegex = new Regex("^\\[(?<provider>[^\\]]+)\\](?:\\[(?<lang>[^\\]]+)\\])?\\s+(?<title>.+?)(?:\\s+(?<chapterNumber>-?\\d+(?:\\.\\d+)?))?\\s*(?:\\((?<chapterName>[^)]+)\\))?$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public SeriesScanner(ILogger<SeriesScanner> logger, ContextProvider baseUrl)
        {
            _parser = new BasicParser();
            _logger = logger;
            _baseUrl = baseUrl;
        }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };


        public async Task<KaizokuInfo?> ProcessDirectoryAsync(List<SuwayomiExtension> exts, string directoryPath, string seriesFolder, CancellationToken token = default)
        {
            string path = seriesFolder[directoryPath.Length..];
            path = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Check if series.json exists
            SeriesInfo? seriesInfo = null;
            var seriesJsonPath = Path.Combine(seriesFolder, "series.json");
            if (File.Exists(seriesJsonPath))
            {
                try
                {
                    var jsonContent = await File.ReadAllTextAsync(seriesJsonPath, token).ConfigureAwait(false);
                    seriesInfo = JsonSerializer.Deserialize<SeriesInfo>(jsonContent, _jsonOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing series.json in {seriesFolder}",seriesFolder);
                }
            }

            // Get all files in this series folder and its subdirectories
            var allFiles = Directory.GetFiles(seriesFolder, "*.*", SearchOption.TopDirectoryOnly);

            // Filter to only archives and supported formats
            var archiveFiles = allFiles.Where(f => Parser.IsArchive(f)).ToList();

            if (archiveFiles.Count == 0)
                return null;  // Skip folders with no archives

            LibraryType[] libraryTypes = { LibraryType.Manga, LibraryType.Comic };
            Dictionary<LibraryType, List<NewDetectedChapter>> detected =
                new Dictionary<LibraryType, List<NewDetectedChapter>>();

            foreach (LibraryType lib in libraryTypes)
            {
                detected[lib] = new List<NewDetectedChapter>();
                foreach (var archiveFile in archiveFiles)
                {
                    FileInfo finfo = new FileInfo(archiveFile);
                    string pre_parsed = archiveFile.RestoreOriginalPathCharacters().Replace("_", " ").Replace("  ", " ");

                    NewDetectedChapter nc = new NewDetectedChapter();
                    string kavname = Path.GetFileNameWithoutExtension(archiveFile);
                    Match kavmatch = KaizokuRegex.Match(kavname);
                    if (kavmatch.Success)
                    {
                        string language = "en";
                        decimal? chapterNumber = null;

                        string[] provider_scanlator = kavmatch.Groups["provider"].Value.Trim().Split("-");
                        if (kavmatch.Groups.TryGetValue("lang", out var kavmatchGroup))
                        {
                            language = kavmatchGroup.Value.Trim();
                        }
                        string seriesTitle = kavmatch.Groups["title"].Value.Trim();

                        if (kavmatch.Groups.TryGetValue("chapterNumber", out var chapterMatchGroup) && !string.IsNullOrEmpty(chapterMatchGroup.Value.Trim()))
                        {
                            chapterNumber = decimal.Parse(chapterMatchGroup.Value.Trim(), CultureInfo.InvariantCulture);
                        }
                        else if (kavmatch.Groups.TryGetValue("chapterName", out var chapterNameGroup))
                        {
                            if (decimal.TryParse(chapterNameGroup.Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedChapter))
                            {
                                chapterNumber = parsedChapter;
                            }
                        }

                        string provider = provider_scanlator[0].Trim();
                        string scanlator = provider_scanlator.Length > 1 ? provider_scanlator[1].Trim() : string.Empty;
                        SuwayomiExtension? ext = exts.FirstOrDefault(a => a.Name.Equals(provider, StringComparison.InvariantCultureIgnoreCase) && a.Lang.Equals(language, StringComparison.InvariantCultureIgnoreCase));
                        if (ext != null)
                        {
                            detected[lib].Add(new NewDetectedChapter
                            {
                                Provider = provider,
                                ProviderThumb = _baseUrl.RewriteExtensionIcon(ext),
                                Scanlator = scanlator,
                                Title = seriesTitle,
                                Language = language.ToLowerInvariant(),
                                Chapter = chapterNumber,
                                IsKaizokuMatch = true,
                                Filename = archiveFile,
                                CreationDate = finfo.CreationTimeUtc
                            });
                            continue;
                        }
                       
                    }

                    // Parse the file using BasicParser
                    var parsedInfo = _parser.Parse(pre_parsed, seriesFolder, lib);
                    decimal chap = 0;
                    if (parsedInfo != null)
                    {
                        _ = decimal.TryParse(!string.IsNullOrEmpty(parsedInfo.Chapters) &&
                                         parsedInfo.Chapters != Parser.DefaultChapter
                            ? parsedInfo.Chapters
                            : "0", out chap);
                    }
                    if (!string.IsNullOrEmpty(parsedInfo?.Scanlator))
                    {
                        string[] provider_scanlator = parsedInfo.Scanlator.Split("-");
                        string provider = provider_scanlator[0].Trim();
                        string scanlator = provider_scanlator.Length > 1 ? provider_scanlator[1].Trim() : string.Empty;
                        SuwayomiExtension? ext = exts.FirstOrDefault(a => a.Name.Equals(provider, StringComparison.InvariantCultureIgnoreCase));
                        if (ext != null)
                        {
                            detected[lib].Add(new NewDetectedChapter
                            {
                                Provider = provider,
                                ProviderThumb = _baseUrl.RewriteExtensionIcon(ext),
                                Scanlator = scanlator,
                                Title = parsedInfo.Series,
                                Language = "en",
                                Chapter = chap,
                                IsKaizokuMatch = true,
                                Filename = archiveFile,
                                CreationDate = finfo.CreationTimeUtc
                            });
                            continue;
                        }
                       
                        parsedInfo.Scanlator = string.Empty;
                    }

                    var d = new NewDetectedChapter
                    {
                        Provider = string.Empty,
                        ProviderThumb = $"{_baseUrl.BaseUrl}serie/thumb/unknown",
                        Scanlator = string.Empty,
                        Title = parsedInfo?.Series ?? "Unknown",
                        Language = "en",
                        Chapter = chap,
                        IsKaizokuMatch = false,
                        Filename = archiveFile,
                        CreationDate = finfo.CreationTimeUtc
                    };

                    if (seriesInfo != null && !string.IsNullOrEmpty(seriesInfo.metadata.name))
                        d.Title = seriesInfo.metadata.name;

                    string[] invalidTitles = new[] { "Chapter", "Ch.", "Episode", "Ep." };
                    if (string.IsNullOrEmpty(d.Title) ||
                        invalidTitles.Any(invalidTitle =>
                            d.Title.StartsWith(invalidTitle, StringComparison.InvariantCultureIgnoreCase) ||
                            d.Title.Equals(invalidTitle, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        int idx = seriesFolder.LastIndexOfAny(['\\', '/']);
                        if (idx >= 0)
                            d.Title = seriesFolder[(idx + 1)..].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        else
                            d.Title = seriesFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        d.Title = d.Title.Replace("_", " ").Replace(".", " ");
                        d.Title = d.Title.Replace("  ", " ").Replace("  ", " ").Trim();
                    }
                    detected[lib].Add(d);
                }
            }

            // Determine the best library type based on detected chapters
            LibraryType flib;
            if (detected[LibraryType.Manga].Select(a => a.Title).Distinct().Count() <
                detected[LibraryType.Comic].Select(a => a.Title).Distinct().Count())
            {
                flib = LibraryType.Manga;
            }
            else if (detected[LibraryType.Comic].Select(a => a.Title).Distinct().Count() <
                     detected[LibraryType.Manga].Select(a => a.Title).Distinct().Count())
            {
                flib = LibraryType.Comic;
            }
            else if (detected[LibraryType.Manga][0].Title.Length <= detected[LibraryType.Comic][0].Title.Length)
                flib = LibraryType.Manga;
            else
                flib = LibraryType.Comic;

            var choose = detected[flib];


            KaizokuInfo? detectedInfo = choose.ToKaizokuInfo();
            if (detectedInfo == null)
                return null;

            detectedInfo.Path = seriesFolder;
            detectedInfo.Type = flib == LibraryType.Manga ? "Manga" : "Comics";

            // Check if kaizoku.json exists
            KaizokuInfo? kaizokuInfo = await seriesFolder.LoadKaizokuInfoFromDirectoryAsync(_logger, token).ConfigureAwait(false);
            if (kaizokuInfo != null)
            {
                 foreach (ProviderInfo info in kaizokuInfo.Providers.ToList())
                {
                    List<ArchiveInfo> archiveInfos = info.Archives.Where(a => !string.IsNullOrEmpty(a.ArchiveName)).ToList();
                    foreach (ArchiveInfo i in archiveInfos)
                    {
                        string fpath = Path.Combine(seriesFolder, i.ArchiveName);
                        if (!File.Exists(fpath))
                        {
                            info.Archives.Remove(i);
                        }
                    }
                    if (info.Archives.All(a => string.IsNullOrEmpty(a.ArchiveName)))
                    {
                        kaizokuInfo.Providers.Remove(info);
                    }
                }

                (_, detectedInfo) = kaizokuInfo.Merge(detectedInfo);

            }
            detectedInfo.Path = path;
            return detectedInfo;
        }

        public async Task RecurseDirectoryAsync(List<KaizokuBackend.Models.Database.Series> allseries, List<SuwayomiExtension> exts,
            List<KaizokuInfo> seriesDict, string directoryPath, string seriesFolder,
            ProgressReporter scanProgress, CancellationToken token = default)
        {
            var seriesFolders = await Task.Run(() => Directory.GetDirectories(seriesFolder, "*.*", SearchOption.AllDirectories), token).ConfigureAwait(false);
            if (seriesFolders.Length == 0)
                return;

            float step = 100 / (float)seriesFolders.Length;
            float acum = 0F;

            foreach (var n in seriesFolders)
            {
                KaizokuInfo? det = await ProcessDirectoryAsync(exts, directoryPath, n, token).ConfigureAwait(false);
                acum += step;

                if (det != null)
                {
                    var seriesComparer = new SeriesComparer();
                    List<KaizokuBackend.Models.Database.Series> findMatchingSeries = seriesComparer.FindMatchingSeries(allseries, det);

                    if (findMatchingSeries.Count > 0)
                    {
                        Dictionary<KaizokuBackend.Models.Database.Series, ArchiveCompare> matches = [];
                        foreach (KaizokuBackend.Models.Database.Series s in findMatchingSeries)
                        {
                            matches.Add(s, seriesComparer.CompareArchives(det, s));
                        }

                        ArchiveCompare bt = ArchiveCompare.Equal;
                        KeyValuePair<KaizokuBackend.Models.Database.Series, ArchiveCompare>? r = matches.FirstOrDefault(a => (a.Value & ArchiveCompare.Equal) == ArchiveCompare.Equal);
                        if (r == null || r?.Key==null)
                        {
                            bt = ArchiveCompare.MissingDB;
                            r = matches.FirstOrDefault(a =>
                                (a.Value & ArchiveCompare.MissingDB) == ArchiveCompare.MissingDB);
                        }

                        if (r == null || r?.Key==null)
                        {
                            bt = ArchiveCompare.MissingArchive;
                            r = matches.FirstOrDefault(a =>
                                (a.Value & ArchiveCompare.MissingDB) == ArchiveCompare.MissingArchive);
                        }

                        if (r != null && r?.Key!=null)
                        {
                            det.ArchiveCompare = bt;
                            det.MatchExisting = r.Value.Key.Id;
                            if (r.Value.Key.StoragePath != det.Path)
                                r.Value.Key.StoragePath = det.Path;
                        }
                    }
                    seriesDict.Add(det);
                    scanProgress.Report(ProgressStatus.InProgress, (decimal)acum, det.Title ?? string.Empty);
                }
            }
        }
    }
}

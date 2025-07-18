
using System.Text.RegularExpressions;

namespace KaizokuBackend.Services.Import.KavitaParser;

/// <summary>
/// This is the basic parser for handling Manga/Comic/Book libraries. This was previously DefaultParser before splitting each parser
/// into their own classes.
/// </summary>
public class BasicParser
{
    public ParserInfo? Parse(string filePath, string rootPath, LibraryType type)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        // TODO: Potential Bug: This will return null, but on Image libraries, if all images, we would want to include this.
        if (type != LibraryType.Image &&
            Parser.IsCoverImage(Path.GetFileName(filePath))) return null;

        var ret = new ParserInfo()
        {
            Filename = Path.GetFileName(filePath),
            Format = Parser.ParseFormat(filePath),
            Title = Parser.RemoveExtensionIfSupported(fileName)!,
            FullFilePath = Parser.NormalizePath(filePath),
            Series = Parser.ParseSeries(fileName, type),
            Chapters = Parser.ParseChapter(fileName, type),
            Scanlator = Parser.ParseScanlator(fileName),
            Volumes = Parser.ParseVolume(fileName, type),
        };
        if (ret.Chapters==Parser.DefaultChapter && !string.IsNullOrEmpty(ret.Scanlator))
        {
            if (Regex.IsMatch(ret.Scanlator, @"^-?\d+(\.\d+)?$"))
            {
                Match m = Regex.Match(ret.Filename, @"\[\d+\]_Chapter_(\d+)");
                if (m.Groups[0].Success)
                {
                    ret.Chapters = m.Groups[1].Value;
                }
                else
                {
                    ret.Chapters = ret.Scanlator;
                }
                ret.Scanlator=string.Empty; 
            }
        }
        if (ret.Series == string.Empty || Parser.IsImage(filePath))
        {
            // Try to parse information out of each folder all the way to rootPath
            ParseFromFallbackFolders(filePath, rootPath, type, ref ret);
        }

        var edition = Parser.ParseEdition(fileName);
        if (!string.IsNullOrEmpty(edition))
        {
            ret.Series = Parser.CleanTitle(ret.Series.Replace(edition, string.Empty), type is LibraryType.Comic);
            ret.Edition = edition;
        }

        var isSpecial = Parser.IsSpecial(fileName, type);
        // We must ensure that we can only parse a special out. As some files will have v20 c171-180+Omake and that
        // could cause a problem as Omake is a special term, but there is valid volume/chapter information.
        if (ret.Chapters == Parser.DefaultChapter && ret.Volumes == Parser.LooseLeafVolume && isSpecial)
        {
            ret.IsSpecial = true;
            ParseFromFallbackFolders(filePath, rootPath, type,
                ref ret); // NOTE: This can cause some complications, we should try to be a bit less aggressive to fallback to folder
        }

        // If we are a special with marker, we need to ensure we use the correct series name. we can do this by falling back to Folder name
        if (Parser.HasSpecialMarker(fileName))
        {
            ret.IsSpecial = true;
            ret.SpecialIndex = Parser.ParseSpecialIndex(fileName);
            ret.Chapters = Parser.DefaultChapter;
            ret.Volumes = Parser.SpecialVolume;

            // NOTE: This uses rootPath. LibraryRoot works better for manga, but it's not always that way.
            // It might be worth writing some logic if the file is a special, to take the folder above the Specials/
            // if present
            var tempRootPath = rootPath;
            if (rootPath.EndsWith("Specials") || rootPath.EndsWith("Specials/"))
            {
                tempRootPath = rootPath.Replace("Specials", string.Empty).TrimEnd('/');
            }

            // Check if the folder the file exists in is Specials/ and if so, take the parent directory as series (cleaned)
            var fileDirectory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(fileDirectory) &&
                (fileDirectory.EndsWith("Specials", StringComparison.OrdinalIgnoreCase) ||
                 fileDirectory.EndsWith("Specials/", StringComparison.OrdinalIgnoreCase)))
            {
                ret.Series = Parser.CleanTitle(Directory.GetParent(fileDirectory)?.Name ?? string.Empty);
            }
            else
            {
                ParseFromFallbackFolders(filePath, tempRootPath, type, ref ret);
            }

            ret.Title = Parser.CleanSpecialTitle(fileName);
        }

        if (string.IsNullOrEmpty(ret.Series))
        {
            ret.Series = Parser.CleanTitle(fileName, type is LibraryType.Comic);
        }

        // Pdfs may have .pdf in the series name, remove that
        if (Parser.IsPdf(filePath) && ret.Series.ToLower().EndsWith(".pdf"))
        {
            ret.Series = ret.Series.Substring(0, ret.Series.Length - ".pdf".Length);
        }

        // Patch in other information from ComicInfo
        UpdateFromComicInfo(ret);
        
        // Patch in information from SeriesInfo
        UpdateFromSeriesInfo(ret);

        if (ret.Volumes == Parser.LooseLeafVolume && ret.Chapters == Parser.DefaultChapter)
        {
            ret.IsSpecial = true;
        }

        // v0.8.x: Introducing a change where Specials will go in a separate Volume with a reserved number
        if (ret.IsSpecial)
        {
            ret.Volumes = Parser.SpecialVolume;
        }

        return ret.Series == string.Empty ? null : ret;
    }

    /// <summary>
    /// Returns a list of folders from end of fullPath to rootPath. If a file is passed at the end of the fullPath, it will be ignored.
    /// Example) (C:/Manga/, C:/Manga/Love Hina/Specials/Omake/) returns [Omake, Specials, Love Hina]
    /// </summary>
    /// <param name="rootPath"></param>
    /// <param name="fullPath"></param>
    /// <returns></returns>
    private static IEnumerable<string> GetFoldersTillRoot(string rootPath, string fullPath)
    {
        var separator = Path.AltDirectorySeparatorChar;
        if (fullPath.Contains(Path.DirectorySeparatorChar))
        {
            fullPath = fullPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (rootPath.Contains(Path.DirectorySeparatorChar))
        {
            rootPath = rootPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var path = fullPath.EndsWith(separator) ? fullPath.Substring(0, fullPath.Length - 1) : fullPath;
        var root = rootPath.EndsWith(separator) ? rootPath.Substring(0, rootPath.Length - 1) : rootPath;
        var paths = new List<string>();
        
        // If a file is at the end of the path, remove it before we start processing folders
        if (Path.GetExtension(path) != string.Empty)
        {
            path = path.Substring(0, path.LastIndexOf(separator));
        }

        while (Path.GetDirectoryName(path) != Path.GetDirectoryName(root))
        {
            var folder = new DirectoryInfo(path).Name;
            paths.Add(folder);
            path = path.Substring(0, path.LastIndexOf(separator));
        }

        return paths;
    }

    /// <summary>
    /// Applicable for everything but ComicVine and Image library types
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="type"></param>
    /// <returns></returns>

    public void ParseFromFallbackFolders(string filePath, string rootPath, LibraryType type, ref ParserInfo ret)
    {
        var fallbackFolders = GetFoldersTillRoot(rootPath, filePath)
            .Where(f => !Parser.IsSpecial(f, type))
            .ToList();

        if (fallbackFolders.Count == 0)
        {
            var rootFolderName = new DirectoryInfo(rootPath).Name;
            var series = Parser.ParseSeries(rootFolderName, type);

            if (string.IsNullOrEmpty(series))
            {
                ret.Series = Parser.CleanTitle(rootFolderName, type is LibraryType.Comic);
                return;
            }

            if (!string.IsNullOrEmpty(series) && (string.IsNullOrEmpty(ret.Series) || !rootFolderName.Contains(ret.Series)))
            {
                ret.Series = series;
                return;
            }
        }

        for (var i = 0; i < fallbackFolders.Count; i++)
        {
            var folder = fallbackFolders[i];

            var parsedVolume = Parser.ParseVolume(folder, type);
            var parsedChapter = Parser.ParseChapter(folder, type);

            if (!parsedVolume.Equals(Parser.LooseLeafVolume) || !parsedChapter.Equals(Parser.DefaultChapter))
            {
                if ((string.IsNullOrEmpty(ret.Volumes) || ret.Volumes.Equals(Parser.LooseLeafVolume))
                    && !string.IsNullOrEmpty(parsedVolume) && !parsedVolume.Equals(Parser.LooseLeafVolume))
                {
                    ret.Volumes = parsedVolume;
                }
                if ((string.IsNullOrEmpty(ret.Chapters) || ret.Chapters.Equals(Parser.DefaultChapter))
                    && !string.IsNullOrEmpty(parsedChapter) && !parsedChapter.Equals(Parser.DefaultChapter))
                {
                    ret.Chapters = parsedChapter;
                }
            }

            // Generally users group in series folders. Let's try to parse series from the top folder
            if (!folder.Equals(ret.Series) && i == fallbackFolders.Count - 1)
            {
                var series = Parser.ParseSeries(folder, type);

                if (string.IsNullOrEmpty(series))
                {
                    ret.Series = Parser.CleanTitle(folder, type is LibraryType.Comic);
                    break;
                }

                if (!string.IsNullOrEmpty(series) && string.IsNullOrEmpty(ret.Series) && !folder.Contains(ret.Series))
                {
                    ret.Series = series;
                    break;
                }
            }
        }
    }

    protected static void UpdateFromComicInfo(ParserInfo info)
    {
        if (info.ComicInfo == null) return;

        if (!string.IsNullOrEmpty(info.ComicInfo.Volume))
        {
            info.Volumes = info.ComicInfo.Volume;
        }
        if (!string.IsNullOrEmpty(info.ComicInfo.Number))
        {
            info.Chapters = info.ComicInfo.Number;
        }
        if (!string.IsNullOrEmpty(info.ComicInfo.Series))
        {
            info.Series = info.ComicInfo.Series.Trim();
        }
        if (!string.IsNullOrEmpty(info.ComicInfo.LocalizedSeries))
        {
            info.LocalizedSeries = info.ComicInfo.LocalizedSeries.Trim();
        }

        if (!string.IsNullOrEmpty(info.ComicInfo.Format) && Parser.HasComicInfoSpecial(info.ComicInfo.Format))
        {
            info.IsSpecial = true;
            info.Chapters = Parser.DefaultChapter;
            info.Volumes = Parser.SpecialVolume;
        }

        // Patch is SeriesSort from ComicInfo
        if (!string.IsNullOrEmpty(info.ComicInfo.SeriesSort))
        {
            info.SeriesSort = info.ComicInfo.SeriesSort.Trim();
        }
    }

    /// <summary>
    /// Updates the ParserInfo with data from the SeriesInfo
    /// </summary>
    /// <param name="info">The ParserInfo to update</param>
    protected static void UpdateFromSeriesInfo(ParserInfo info)
    {
        if (info.SeriesInfo == null) return;

        // Series name takes precedence if it exists in the SeriesInfo
        if (!string.IsNullOrEmpty(info.SeriesInfo.metadata.name))
        {
            info.Series = info.SeriesInfo.metadata.name.Trim();
        }

        // If we have a publisher, add it to the metadata
        if (!string.IsNullOrEmpty(info.SeriesInfo.metadata.publisher))
        {
            info.Publisher = info.SeriesInfo.metadata.publisher;
        }
    }

    protected static bool IsEmptyOrDefault(string volumes, string chapters)
    {
        return (string.IsNullOrEmpty(chapters) || chapters == Parser.DefaultChapter) &&
               (string.IsNullOrEmpty(volumes) || volumes == Parser.LooseLeafVolume);
    }
}
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for file system operations
    /// </summary>
    public static class FileSystemExtensions
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = true, 
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull 
        };

        /// <summary>
        /// Loads KaizokuInfo from a directory's kaizoku.json file
        /// </summary>
        /// <param name="seriesFolder">Path to the series folder</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>KaizokuInfo object or null if not found/invalid</returns>
        public static async Task<KaizokuInfo?> LoadKaizokuInfoFromDirectoryAsync(this string seriesFolder, ILogger? logger = null, CancellationToken token = default)
        {
            var kaizokuJsonPath = Path.Combine(seriesFolder, "kaizoku.json");
            if (!File.Exists(kaizokuJsonPath))
            {
                return null;
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(kaizokuJsonPath, token).ConfigureAwait(false);
                return JsonSerializer.Deserialize<KaizokuInfo>(jsonContent);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Error parsing kaizoku.json in {seriesFolder}: {message}", seriesFolder, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Saves KaizokuInfo to a directory's kaizoku.json file
        /// </summary>
        /// <param name="info">KaizokuInfo object to save</param>
        /// <param name="seriesFolder">Path to the series folder</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <param name="token">Cancellation token</param>
        public static async Task SaveKaizokuInfoToDirectoryAsync(this KaizokuInfo info, string seriesFolder, ILogger? logger = null, CancellationToken token = default)
        {
            var kaizokuJsonPath = Path.Combine(seriesFolder, "kaizoku.json");
            try
            {
                var jsonContent = JsonSerializer.Serialize(info, JsonOptions);
                await File.WriteAllTextAsync(kaizokuJsonPath, jsonContent, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError("Error saving kaizoku.json to {seriesFolder}: {message}", seriesFolder, ex.Message);
            }
        }

        /// <summary>
        /// Saves KaizokuInfo to a series directory
        /// </summary>
        /// <param name="series">The Series entity</param>
        /// <param name="seriesFolder">Path to the series folder</param>
        /// <param name="logger">Optional logger</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Task</returns>
        public static Task SaveKaizokuInfoToDirectoryAsync(this Series series, string seriesFolder, ILogger? logger = null, CancellationToken token = default)
        {
            return series.ToKaizokuInfo().SaveKaizokuInfoToDirectoryAsync(seriesFolder, logger, token);
        }
        /// <summary>
        /// Gets an embedded resource stream
        /// </summary>
        /// <param name="resourceName">Name of the embedded resource</param>
        /// <returns>Stream of the resource or null if not found</returns>
        public static Stream? StreamEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourcePath = $"KaizokuBackend.Resources.{resourceName}";
            return assembly.GetManifestResourceStream(resourcePath);
        }
        /// <summary>
        /// Builds a storage path for a series based on settings, type, and title
        /// </summary>
        /// <param name="title">Series title</param>
        /// <param name="type">Series type (optional)</param>
        /// <returns>Full storage path</returns>
        public static string BuildStoragePath(this string title, string? type, Settings settings)
        {
            var baseStorageFolder = settings.StorageFolder;

            // Create a filename-safe version of the title
            var safeTitle = title.MakeFolderNameSafe();

            // Build the path components
            string path;
            if (!string.IsNullOrWhiteSpace(type))
            {
                // Include type in path if it exists
                var safeType = type.MakeFolderNameSafe();
                path = Path.Combine(baseStorageFolder, safeType, safeTitle);
            }
            else
            {
                // Just use base folder and title
                path = Path.Combine(baseStorageFolder, safeTitle);
            }

            return path;
        }

        /// <summary>
        /// Rewrites a SuwayomiSeries thumbnail URL to a Kaizoku path
        /// </summary>
        /// <param name="series">The SuwayomiSeries</param>
        /// <returns>Rewritten path</returns>
        public static string RewriteToKaizokuPath(this SuwayomiSeries series)
        {
            if (series.ThumbnailUrl == null)
                return "serie/thumb/unknown";

            string thumb = series.ThumbnailUrl[(series.ThumbnailUrl.IndexOf("/manga/", StringComparison.InvariantCulture) + 6)..];
            thumb = thumb[..thumb.LastIndexOf('/')];
            return $"serie/thumb{thumb}!{series.ThumbnailUrlLastFetched}";
        }


        /// <summary>
        /// Checks if a directory exists at the given newPath (case-insensitive) under basePath,
        /// and returns the actual path with correct casing if found, or the combined path if not.
        /// </summary>
        /// <param name="basePath">The base directory path.</param>
        /// <param name="newPath">The directory path to check, relative to basePath.</param>
        /// <returns>The actual path with correct casing if found, otherwise the combined path.</returns>
        public static string GetActualDirectoryPathCaseInsensitive(this string basePath, string newPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(newPath))
                throw new ArgumentException("basePath and newPath must be non-empty");

            var parts = newPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(p => !string.IsNullOrEmpty(p)).ToArray();

            string currentPath = basePath;
            List<string> actualParts = new List<string>();

            foreach (var part in parts)
            {
                if (!Directory.Exists(currentPath))
                    break;

                var dirs = Directory.GetDirectories(currentPath);
                var match = dirs.FirstOrDefault(d =>
                    string.Equals(Path.GetFileName(d), part, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    actualParts.Add(Path.GetFileName(match));
                    currentPath = match;
                }
                else
                {
                    // Not found, append the rest as-is
                    actualParts.Add(part);
                    currentPath = Path.Combine(currentPath, part);
                }
            }

            // If there are remaining parts, append them as-is
            if (actualParts.Count < parts.Length)
                actualParts.AddRange(parts.Skip(actualParts.Count));

            return Path.Combine(actualParts.ToArray());
        }
        public static readonly Dictionary<string, string> InvalidPathCharacterMap = new()
        {
            { "*", "\u2605" },
            { "|", "\u00a6" },
            { "\\", "\u29F9" },
            { "/", "\u2044" },  
            { ":", "\u0589" },
            { "\"", "\u2033" },
            { ">", "\u203a" },
            { "<", "\u2039" },
            { "?", "\uff1f" },
        };

        public static readonly Dictionary<string, string> ReversePathCharacterMap =
            InvalidPathCharacterMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        public static string ReplaceInvalidFilenameAndPathCharacters(this string path)
        {
            // Ensure the static constructor is called
            if (string.IsNullOrEmpty(path))
                return path;
            var ret = path;
            foreach (var kvp in InvalidPathCharacterMap)
                ret = ret.Replace(kvp.Key, kvp.Value);
            ret = ret.Replace("...", "\u2026");
            ret = ret.Trim('.');
            return ret.Trim().Normalize(NormalizationForm.FormC);
        }

        public static string RestoreOriginalPathCharacters(this string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var ret = path;
            foreach (var kvp in ReversePathCharacterMap)
                ret = ret.Replace(kvp.Key, kvp.Value);
            ret = ret.Replace("\u2026", "..."); // … → ...
            return ret.Trim();
        }

        public static void DeletePhysicalSeries(this Series dbSeries, Settings settings, ILogger? logger)
        {
            if (string.IsNullOrEmpty(dbSeries.StoragePath))
                return;
            string seriesPath = Path.Combine(settings.StorageFolder, dbSeries.StoragePath);
            if (!Directory.Exists(seriesPath))
                return;
            logger?.LogInformation("Deleting Series {Title} in path {seriesPath}.", dbSeries.Title, seriesPath);
            List<string> files = dbSeries.Sources.SelectMany(a => a.Chapters).Where(a => !string.IsNullOrEmpty(a.Filename))
                .Select(a => a.Filename!).ToList();
            foreach (string file in files)
            {
                string fullPath = Path.Combine(seriesPath, file);
                try
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch (Exception)
                {
                    logger?.LogWarning("Unable to delete {fullpath}.", fullPath);
                }
            }
            string[] filesLeft = Directory.GetFileSystemEntries(seriesPath, "*.*", SearchOption.AllDirectories);
            if (filesLeft.Length == 0)
            {
                try
                {
                    Directory.Delete(seriesPath, true);
                }
                catch (Exception e)
                {
                    logger?.LogWarning(e, "Unable to delete directory {seriesPath}.", seriesPath);
                }
            }
        }
    }
}
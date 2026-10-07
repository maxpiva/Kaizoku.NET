using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RensaioBackend.Extensions
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
        /// Loads ImportSeriesSnapshot from a directory's rensaio.json file
        /// </summary>
        /// <param name="seriesFolder">Path to the series folder</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>ImportSeriesSnapshot object or null if not found/invalid</returns>
        public static async Task<ImportSeriesSnapshot?> LoadImportSeriesSnapshotFromDirectoryAsync(this string seriesFolder, ILogger? logger = null, CancellationToken token = default)
        {
            var rensaioJsonPath = Path.Combine(seriesFolder, "rensaio.json");
            if (!File.Exists(rensaioJsonPath))
            {
                return null;
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(rensaioJsonPath, token).ConfigureAwait(false);
                return JsonSerializer.Deserialize<ImportSeriesSnapshot>(jsonContent);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Error parsing rensaio.json in {seriesFolder}: {message}", seriesFolder, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Gets an embedded resource stream
        /// </summary>
        /// <param name="resourceName">Name of the embedded resource</param>
        /// <returns>Stream of the resource or null if not found</returns>
        public static Stream StreamEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourcePath = $"RensaioBackend.Resources.{resourceName}";
            return assembly.GetManifestResourceStream(resourcePath)!;
        }
        /// <summary>
        /// Builds a storage path for a series based on settings, type, and title
        /// </summary>
        /// <param name="title">Series title</param>
        /// <param name="type">Series type (optional)</param>
        /// <returns>Full storage path</returns>
        public static string BuildStoragePath(this string title, string? type, SettingsDto settings)
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
            ret = ret.Replace("\u2026", "..."); // � ? ...
            return ret.Trim();
        }

        /// <summary>Resolve a strict child of the library, rejecting traversal and symlink ancestors.</summary>
        public static string ResolveSafeSeriesPath(string library, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new IOException("Physical series path must be a non-empty relative library path.");
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(library));
            string target = Path.GetFullPath(Path.Combine(root, relativePath));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new IOException("Physical series path must remain inside the library (not the library root).");

            // Check the configured root's ancestors too: a library behind a symlink is not
            // a safe deletion boundary. NFS mount points are directories, not symlinks.
            for (DirectoryInfo? directory = new DirectoryInfo(target); directory != null; directory = directory.Parent)
            {
                try
                {
                    if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Refusing physical operation through a symbolic link: " + directory.FullName);
                }
                catch (DirectoryNotFoundException) { }
                catch (FileNotFoundException) { }
            }
            return target;
        }

        public static void DeletePhysicalSeries(this SeriesEntity dbSeries, SettingsDto settings, ILogger? logger)
        {
            string seriesPath = ResolveSafeSeriesPath(settings.StorageFolder, dbSeries.StoragePath);
            try
            {
                // GetAttributes (rather than Exists) preserves permission and I/O errors.
                if ((File.GetAttributes(seriesPath) & FileAttributes.Directory) == 0)
                    throw new IOException("The physical series path is not a directory.");
            }
            catch (DirectoryNotFoundException) { return; }
            catch (FileNotFoundException) { return; }

            // Preflight the entire tree before removing anything. Never follow a nested
            // symlink, even if it currently points inside the library.
            var directories = new Stack<string>();
            directories.Push(seriesPath);
            while (directories.TryPop(out string? directory))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Refusing to delete a series containing a symbolic link: " + entry);
                    if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
                }
            }

            logger?.LogInformation("Deleting Series {Title} in path {seriesPath}.", dbSeries.Title, seriesPath);
            // The user selected physical deletion of the series folder, including covers,
            // rensaio.json and untracked files. Failures propagate, so DB removal cannot run.
            Directory.Delete(seriesPath, recursive: true);
        }
    }
}

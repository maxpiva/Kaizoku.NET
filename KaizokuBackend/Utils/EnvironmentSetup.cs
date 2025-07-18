using KaizokuBackend.Services;
using Microsoft.AspNetCore.Razor.Runtime.TagHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Filters;
using Serilog.Settings.Configuration;
using Serilog.Sinks.SystemConsole.Themes;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KaizokuBackend.Utils
{

    public static class EnvironmentSetup
    {
        public const string SuwayomiJar = "Suwayomi-Server-{version}.jar";
        public const string SuwayomiJarUrl = "https://github.com/Suwayomi/Suwayomi-Server/releases/download/{version}/{jar}";
        public const string SuwayomiJarPreviewUrl = "https://github.com/Suwayomi/Suwayomi-Server-preview/releases/download/{version}/{jar}";

        public const string AppKaizokuNET = "Kaiz.NET";
        public const string AppSuwayomi = "Suwayomi";

        public const string AppSettings = "appsettings.json";
        public const string SuwayomiConfig = "server.conf";

        public const string wwwRootSHA256 = "wwwroot.sha256";
        public const string wwwRootZip = "wwwroot.zip";
        /// <summary>
        /// Gets the resolved path to the application's data directory.
        /// </summary>
        public static string Path { get; }

        public static string JavaRunner { get; set; } = "java";

        public static IConfiguration? Configuration { get; private set; }

        private static ILogger? _logger = null;
        public static ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerInfrastructure.CreateAppLogger(AppKaizokuNET,nameof(EnvironmentSetup)); ;
                }
                return _logger;
            }
        }

        
        static EnvironmentSetup()
        {
            Path = ResolveDataDirectory();
        }

        public static async Task WriteToAppSettingsAsync(string? storageDirectory, CancellationToken token = default)
        {
          

            if (storageDirectory == null)
                storageDirectory = Environment.GetEnvironmentVariable("KAIZOKU_STORAGEDIR");
            if (storageDirectory==null && IsDocker)
            {
                storageDirectory = "/series";
            }
            var destAppSettingsPath = System.IO.Path.Combine(Path, AppSettings);
            var sourceAppSettingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, AppSettings);

            JsonNode? destinationJson;
            JsonNode? sourceJson = null;

            string logsDir = System.IO.Path.Combine(Path, "logs");
            if (!System.IO.Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);

            // Read source appsettings.json if it exists
            if (File.Exists(sourceAppSettingsPath))
            {
                try
                {
                    var sourceContent = await File.ReadAllTextAsync(sourceAppSettingsPath, token);
                    sourceJson = JsonNode.Parse(sourceContent);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to read source appsettings.json from {SourcePath}", sourceAppSettingsPath);
                }
            }
            else
            {
                string resourceName = nameof(KaizokuBackend) + "." + AppSettings;
                using Stream? stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    var sourceContent = await new StreamReader(stream).ReadToEndAsync(token).ConfigureAwait(false);
                    sourceJson = JsonNode.Parse(sourceContent);
                }
                else
                {
                    Logger.LogWarning("The initial appsettings.json was not found in the application's resources.");
                }
            }

            // Read or create destination appsettings.json
            if (File.Exists(destAppSettingsPath))
            {
                try
                {
                    var destContent = await File.ReadAllTextAsync(destAppSettingsPath, token);
                    destinationJson = JsonNode.Parse(destContent)!;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to read destination appsettings.json from {DestPath}, will recreate", destAppSettingsPath);
                    // If we can't read the destination, use source as base or create empty
                    destinationJson = sourceJson?.DeepClone() ?? new JsonObject();
                }
            }
            else
            {
                // If destination doesn't exist, use source as base or create empty
                destinationJson = sourceJson?.DeepClone() ?? new JsonObject();
            }

            bool updated = false;

            // Merge new properties from source to destination if both exist
            if (sourceJson != null && destinationJson != null)
            {
                updated = MergeJsonNodes(sourceJson, destinationJson) || updated;
            }

            if (destinationJson != null)
            {
                // Apply runtime-specific modifications
                if (!string.IsNullOrEmpty(storageDirectory) && Directory.Exists(storageDirectory))
                {
                    destinationJson["StorageFolder"] = storageDirectory;
                    updated = true;
                }

                var connectionStrings = destinationJson["ConnectionStrings"];
                if (connectionStrings != null)
                {
                    string currentDb = connectionStrings["DefaultConnection"]?.ToString() ?? "";
                    string expectedRelativeDb = "Data Source=kaizoku.db";
                    string expectedAbsoluteDb = "Data Source=" + System.IO.Path.Combine(Path, "kaizoku.db");

                    // Update if it's still the template value or if it's the relative path
                    if (currentDb == expectedRelativeDb)
                    {
                        connectionStrings["DefaultConnection"] = expectedAbsoluteDb;
                        updated = true;
                    }
                }

                var seriLog = destinationJson["Serilog"];
                if (seriLog != null)
                {
                    var writeTo = seriLog["WriteTo"];
                    if (writeTo != null)
                    {
                        foreach (var n in writeTo.AsArray())
                        {
                            if (n==null)
                                continue;
                            var name = n["Name"];
                            if (name != null && name?.ToString() == "File")
                            {
                                var args = n["Args"];
                                if (args != null)
                                {
                                    var path = args["path"];
                                    string expectedPath = "logs/log-.txt";
                                    string? nPath = path?.ToString();
                                    string expectedAbsoluteDb = System.IO.Path.Combine(Path,
                                        "logs" + System.IO.Path.DirectorySeparatorChar + "log-.txt");
                                    if (nPath != null && nPath == expectedPath)
                                    {
                                        args["path"] = expectedAbsoluteDb;
                                        updated = true;
                                    }
                                }
                            }
                        }


                    }
                }
            }

            // Write back the merged and modified content if there were any updates
            if (updated && destinationJson!=null)
            {
                try
                {
                    await File.WriteAllTextAsync(destAppSettingsPath, destinationJson.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }), token);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to write updated appsettings.json to {DestPath}", destAppSettingsPath);
                    throw;
                }
            }
        }

        /// <summary>
        /// Merges properties from source JsonNode into destination JsonNode.
        /// Only adds new properties that don't exist in destination.
        /// </summary>
        /// <param name="source">Source JSON node</param>
        /// <param name="destination">Destination JSON node to merge into</param>
        /// <returns>True if any changes were made</returns>
        private static bool MergeJsonNodes(JsonNode source, JsonNode destination)
        {
            bool hasChanges = false;

            if (source is JsonObject sourceObj && destination is JsonObject destObj)
            {
                foreach (var sourceProperty in sourceObj)
                {
                    string propertyName = sourceProperty.Key;
                    JsonNode? sourceValue = sourceProperty.Value;

                    if (sourceValue == null) continue;

                    if (!destObj.ContainsKey(propertyName))
                    {
                        // Property doesn't exist in destination, add it
                        destObj[propertyName] = sourceValue.DeepClone();
                        hasChanges = true;
                    }
                    else
                    {
                        JsonNode? destValue = destObj[propertyName];
                        if (destValue != null)
                        {
                            // Property exists, recurse for objects, skip for primitives to preserve user settings
                            if (sourceValue is JsonObject && destValue is JsonObject)
                            {
                                bool childChanged = MergeJsonNodes(sourceValue, destValue);
                                hasChanges = hasChanges || childChanged;
                            }
                            // For arrays and primitive values, we keep the destination values
                            // to preserve user configurations
                        }
                    }
                }
            }

            return hasChanges;
        }

        public static bool CheckIfRootDirExists()
        {
            CreateBaseDirectoryIfNeeded();
            CopyInitialAppSettings();
            BuildConfiguration();
            string storageFolder = Configuration!.GetValue<string>("StorageFolder", string.Empty);
            return !string.IsNullOrEmpty(storageFolder);
        }


        /// <summary>
        /// Initializes the data directory by creating it if it doesn't exist
        /// and copying the initial configuration file.
        /// </summary>
        public static async Task InitializeAsync(string? storageDirectory = null, CancellationToken token = default)
        {
            CreateBaseDirectoryIfNeeded();
            CopyInitialAppSettings();
            await WriteToAppSettingsAsync(storageDirectory, token);
            BuildConfiguration();
            LoggerInfrastructure.BuildLogger(Configuration!);
            ExtractWWWRoot();
            CopyInitialSuwayomiConfig();
            if (!CheckJavaVersion())
            {
                Logger.LogError("Java Runtime Environment (JRE) 21 or later is required to run Suwayomi. Please install Java 21 or later.");
                throw new InvalidOperationException("Java Runtime Environment (JRE) 21 or later is required.");
            }
            if (!await DownloadSuwayomiIfNeededAsync(token))
            {
                Logger.LogError("Unable to download Suwayomi, check if the version and preview settings are allright and the app have internet connection, otherwise download manually and put it in the Suwayomi Directory make sure versions match.");
                throw new InvalidOperationException("Unable to download Suwayomi, check if the version and preview settings are allright and the app have internet connection, otherwise download manually and put it in the Suwayomi Directory make sure versions match.");
            }
        }

        /// <summary>
        /// Checks if another instance of the application is already running
        /// </summary>
        /// <returns>True if another instance is running, false otherwise</returns>
        public static bool IsApplicationAlreadyRunning()
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                string processName = currentProcess.ProcessName;

                // Get all processes with the same name
                var processes = Process.GetProcessesByName(processName);

                // Check if there are other processes with the same name but different PID
                bool hasOtherInstance = processes.Any(p => p.Id != currentProcess.Id);

                // Clean up the process array
                foreach (var process in processes)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        process.Dispose();
                    }
                }

                return hasOtherInstance;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error checking if application is already running: {Message}", ex.Message);
                return false;
            }
        }

        public static void ExtractWWWRoot()
        {
            string outputDir = System.IO.Path.Combine(Configuration!["runtimeDirectory"]!, "wwwroot");
            if (!Directory.Exists(outputDir))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                }
                catch (Exception)
                {
                    Logger.LogError("Unable to create wwwroot {outputDir}.", outputDir);
                    throw new InvalidOperationException("Unable to create wwwroot.");
                }
            }
            Assembly assembly = Assembly.GetExecutingAssembly()!;
            Stream? sha256Stream = assembly.GetManifestResourceStream(nameof(KaizokuBackend) + "."+wwwRootSHA256);
            if (sha256Stream == null)
            {
                Logger.LogError("Unable to find wwwroot SHA256 version");
                throw new InvalidOperationException("Unable to find wwwroot SHA256 version.");
            }
            string sha256 = new StreamReader(sha256Stream).ReadToEnd().Trim();
            string sha256Path =System.IO.Path.Combine(outputDir, wwwRootSHA256);
            if (File.Exists(sha256Path))
            {
                string sha256Current = File.ReadAllText(sha256Path).Trim();
                if (sha256 == sha256Current)
                    return;
            }
            Stream? wwwStream = assembly.GetManifestResourceStream(nameof(KaizokuBackend) + "." + wwwRootZip);
            if (wwwStream == null)
            {
                Logger.LogError("Unable to find wwwroot.zip as embedded resource.");
                throw new InvalidOperationException("Unable to find wwwroot.zip as embedded resource.");
            }

            using var archive = ArchiveFactory.Open(wwwStream);
            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                string fullPath = System.IO.Path.Combine(outputDir, entry.Key!);
                fullPath = fullPath.Replace('/', System.IO.Path.DirectorySeparatorChar).Replace('\\', System.IO.Path.DirectorySeparatorChar);
                string dir = System.IO.Path.GetDirectoryName(fullPath)!;
                if(!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                entry.WriteToFile(fullPath, new ExtractionOptions()
                {
                    ExtractFullPath = false,
                    Overwrite = true
                });

            }

            File.WriteAllText(sha256Path, sha256);

        }
        /// <summary>
        /// Checks if Java Runtime Environment (JRE) 21 or later is available
        /// </summary>
        /// <returns>True if JRE 21 or later is available, false otherwise</returns>
        public static bool CheckJavaVersion()
        {
            Logger.LogInformation("Checking if JRE 21 is installed.");

            try
            {
                // Try to execute 'java -version' command
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    Logger.LogWarning("Java command not found in PATH.");
                    return false;
                }

                // Java version information is typically output to stderr
                string output = process.StandardError.ReadToEnd();
                string stdOut = process.StandardOutput.ReadToEnd();
                
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Logger.LogWarning("Java command failed with exit code {ExitCode}", process.ExitCode);
                    return false;
                }

                // Combine both outputs as Java version can appear in either
                string fullOutput = output + " " + stdOut;
                
                // Parse the Java version from the output
                var version = ParseJavaVersion(fullOutput);
                
                if (version.HasValue)
                {
                    Logger.LogInformation("Found Java version {Version}", version.Value);
                    return version.Value >= 21;
                }
                else
                {
                    Logger.LogWarning("Unable to parse Java version from output: {Output}", fullOutput);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error checking Java version: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Parses the Java version from the output of 'java -version'
        /// </summary>
        /// <param name="versionOutput">The output from java -version command</param>
        /// <returns>The major version number if successfully parsed, null otherwise</returns>
        private static int? ParseJavaVersion(string versionOutput)
        {
            if (string.IsNullOrWhiteSpace(versionOutput))
                return null;

            // Java version patterns:
            // Java 8: java version "1.8.0_XXX"
            // Java 9+: java version "11.0.XX", "17.0.XX", "21.0.XX", etc.
            // OpenJDK: openjdk version "11.0.XX", "17.0.XX", "21.0.XX", etc.
            
            // Look for version patterns
            var patterns = new[]
            {
                // Pattern for Java 9+ (e.g., "21.0.1", "17.0.8")
                @"(?:java|openjdk)\s+version\s+""(\d+)\.[\d\.]+.*?""",
                // Pattern for Java 8 and below (e.g., "1.8.0_XXX")
                @"(?:java|openjdk)\s+version\s+""1\.(\d+)\.[\d_\.]+.*?""",
                // Alternative pattern without quotes
                @"(?:java|openjdk)\s+(\d+)\.[\d\.]+",
                // Pattern for newer format without "version" keyword
                @"""(\d+)\.[\d\.]+.*?"""
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(versionOutput, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int version))
                {
                    // For Java 8 and below, the version number is in the second group
                    // For Java 9+, it's directly the major version
                    if (pattern.Contains("1\\."))
                    {
                        // This is the Java 8 pattern (1.8.0_XXX), so the actual version is the second number
                        return version;
                    }
                    else
                    {
                        // This is Java 9+ pattern
                        return version;
                    }
                }
            }

            return null;
        }

        public static async Task<bool> DownloadSuwayomiIfNeededAsync(CancellationToken token = default)
        {
            _logger?.LogInformation("Checking if Suwayomi is downloaded and up to date.");
            bool useCustomApi = Configuration!.GetValue<bool>("Suwayomi:UseCustomApi", false);
            if (useCustomApi)
                return true;
            bool usePreview = Configuration!.GetValue<bool>("Suwayomi:UsePreview", false);
            string version = Configuration!.GetValue<string>("Suwayomi:Version", "v2.0.1727");
            string jar = SuwayomiJar;
            string url = SuwayomiJarUrl;
            if (usePreview)
                url = SuwayomiJarPreviewUrl;
            jar = jar.Replace("{version}", version);
            url = url.Replace("{version}", version).Replace("{jar}", jar);
            string suwayomiPath = System.IO.Path.Combine(Path, "Suwayomi");
            string suwayomiJarFullPath = System.IO.Path.Combine(suwayomiPath, jar);
            if (File.Exists(suwayomiJarFullPath))
                return true;


            //Delete other versions
            string[] jars = Directory.GetFiles(suwayomiPath, "*.jar", SearchOption.TopDirectoryOnly);
            foreach (string s in jars)
            {
                try { File.Delete(s); } catch (Exception) { }
            }
            _logger?.LogInformation("Downloading Suwayomi version {version} ...", version);

            return await DownloadFileAsync(url, jar, suwayomiPath, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads a file from the internet and saves it to the specified directory
        /// </summary>
        /// <param name="url">The URL to download from</param>
        /// <param name="fileName">The name of the file to save</param>
        /// <param name="destinationDirectory">The directory to save the file to</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the download was successful, false otherwise</returns>
        public static async Task<bool> DownloadFileAsync(string url, string fileName, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty", nameof(fileName));
            
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new ArgumentException("Destination directory cannot be null or empty", nameof(destinationDirectory));

            try
            {
                // Ensure the destination directory exists
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                // Combine the directory and filename to get the full path
                var filePath = System.IO.Path.Combine(destinationDirectory, fileName);
                if (File.Exists(filePath))
                    return true;
                HttpClient httpClient = new HttpClient();

                // Download the file
                using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                // Save the file to disk
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                
                return true;
            }
            catch (Exception)
            {
                // Return false on any exception (network issues, file system issues, etc.)
                return false;
            }
        }
        public static bool FileExistsEvenIfNoAccess(string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                return fileInfo.Exists;
            }
            catch (UnauthorizedAccessException)
            {
                // File likely exists but we have no permission
                return true;
            }
            catch (PathTooLongException)
            {
                // Considered invalid
                return false;
            }
            catch (Exception)
            {
                // Other unexpected issues
                return false;
            }
        }
        public static bool IsDocker
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return FileExistsEvenIfNoAccess("/.dockerenv");
                return false;
            }
        }

        private static void CreateBaseDirectoryIfNeeded()
        {
            if (!Directory.Exists(Path))
                Directory.CreateDirectory(Path);
        }

        /// <summary>
        /// Resolves the appropriate data directory path based on the operating system and environment variables.
        /// </summary>
        /// <returns>The resolved data directory path.</returns>
        private static string ResolveDataDirectory()
        {
            // Check for the KAIZOKU_DATADIR environment variable, primarily for Docker containers.
            var dataDir = Environment.GetEnvironmentVariable("KAIZOKU_DATADIR");

            if (!string.IsNullOrEmpty(dataDir))
            {
                return dataDir;
            }

            if (IsDocker)
            {
                dataDir = "/config";
                return dataDir;
            }

            string basePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // For Windows, use the LocalApplicationData folder.
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else
            {
                // For macOS and Linux, use the .conf directory in the user's home folder.
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                basePath = System.IO.Path.Combine(basePath, ".conf");
            }

            return System.IO.Path.Combine(basePath, "KaizokuNET");
        }

        /// <summary>
        /// Copies the initial appsettings.json to the data directory if it doesn't already exist.
        /// </summary>
        /// <exception cref="FileNotFoundException">Thrown if the source appsettings.json cannot be found.</exception>
        private static void CopyInitialAppSettings()
        {
            var destAppSettingsPath = System.IO.Path.Combine(Path, "appsettings.json");
            if (File.Exists(destAppSettingsPath))
            {
                return;
            }

            var sourceAppSettingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(sourceAppSettingsPath))
            {
                File.Copy(sourceAppSettingsPath, destAppSettingsPath);
            }
            else
            {
                string resourceName = nameof(KaizokuBackend) + "." + AppSettings;
                using Stream? stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using Stream destStream = File.Create(destAppSettingsPath);
                    stream.CopyTo(destStream);
                }
                else
                {
                    Logger.LogWarning("The initial appsettings.json was not found in the application's resources.");
                    throw new FileNotFoundException("The initial appsettings.json was not found in the application's resources.", sourceAppSettingsPath);
                }
            }
        }

        public static IConfigurationBuilder AddConfigurations(IConfigurationBuilder builder)
        {
            builder.AddEnvironmentVariables();
            builder.SetBasePath(Path).AddJsonFile($"appsettings.json", optional: false, reloadOnChange: true);
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "runtimeDirectory",  Path }
            });
            return builder;
        }
        private static void BuildConfiguration()
        {
            var builder = new ConfigurationBuilder();
            AddConfigurations(builder);
            Configuration = builder.Build();
        }





        private static void CopyInitialSuwayomiConfig()
        {
            Logger.LogInformation("Initializing Suwayomi Configuration.");
            string suwayomiPath = System.IO.Path.Combine(Path, "Suwayomi");

            if (!Directory.Exists(suwayomiPath))
            {
                Directory.CreateDirectory(suwayomiPath);
            }
            var serverConfig = System.IO.Path.Combine(suwayomiPath, SuwayomiConfig);
            if (File.Exists(serverConfig))
            {
                return;
            }
            var sourceConfig = System.IO.Path.Combine(AppContext.BaseDirectory, AppSuwayomi, SuwayomiConfig);
            if (File.Exists(sourceConfig))
            {
                File.Copy(sourceConfig, serverConfig);
            }
            else
            {
                string resourceName = nameof(KaizokuBackend) + "." + AppSuwayomi + "." + SuwayomiConfig;
                using Stream? stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using FileStream dstream  = File.Create(serverConfig);
                    stream.CopyTo(dstream);
                }
                else
                {
                    Logger.LogError("The initial Suwayomi server.conf was not found in '{sourceConfig}'.", sourceConfig);
                    throw new FileNotFoundException($"The initial Suwayomi server.conf was not found in '{sourceConfig}'.");
                }
            }
        }
    }

   

}

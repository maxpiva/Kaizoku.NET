using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Net.Http.Headers;
using Mihon.ExtensionsBridge.IKVMCompiler.Models;
using Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;

namespace Mihon.ExtensionsBridge.IKVMCompiler.Services;



/// <summary>
/// Provides functionality to download and provision the IKVM compiler toolchain dynamically.
/// </summary>
/// <remarks>
/// This class handles downloading the IKVM tools and JRE packages from GitHub releases,
/// extracting them to the appropriate directories, and managing version tracking to avoid
/// unnecessary re-downloads.
/// </remarks>
public class IkvmCompilerDownloader : IIkvmCompilerDownloader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly IIKVMVersion _version;
    private readonly ICompilerWorkingFolderStructure _folder;

    /// <summary>
    /// Initializes a new instance of the <see cref="IkvmCompilerDownloader"/> class.
    /// </summary>
    /// <param name="factory">The HTTP client factory for creating HTTP clients.</param>
    /// <param name="version">The IKVM version configuration.</param>
    /// <param name="folder">The working folder structure for storing IKVM files.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="factory"/>, <paramref name="version"/>, 
    /// <paramref name="folder"/>, or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public IkvmCompilerDownloader(IHttpClientFactory factory, IIKVMVersion version, ICompilerWorkingFolderStructure folder, ILogger<IkvmCompilerDownloader> logger)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));
        if (version == null)
            throw new ArgumentNullException(nameof(version));
        if (folder == null)
            throw new ArgumentNullException(nameof(folder));
        _httpClientFactory = factory;
        _logger = logger;
        _version = version;
        _folder = folder;

    }

    /// <summary>
    /// Downloads and provisions the IKVM compiler toolchain if needed.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous download and extraction operation.</returns>
    /// <remarks>
    /// This method checks if the correct version of IKVM is already provisioned by examining
    /// the version.json file. If the versions match and directories exist, the download is skipped.
    /// Otherwise, it downloads both the IKVM tools and JRE packages in parallel, extracts them,
    /// and writes a version.json file to track the installed version.
    /// </remarks>
    /// <exception cref="HttpRequestException">Thrown when the download fails.</exception>
    /// <exception cref="InvalidOperationException">Thrown when archive extraction encounters security issues.</exception>
    public async Task CompilerDownloadAsync(CancellationToken cancellationToken = default)
    {
        // Check if download is needed
        var versionFilePath = Path.Combine(_folder.IKVMFolder, "version.json");
        var needsDownload = false;

        if (File.Exists(versionFilePath))
        {
            try
            {
                var existingVersionJson = await File.ReadAllTextAsync(versionFilePath, cancellationToken).ConfigureAwait(false);
                var existingVersion = System.Text.Json.JsonSerializer.Deserialize<JsonIKVMVersion>(existingVersionJson);

                if (existingVersion == null ||
                    existingVersion.Version != _version.Version ||
                    existingVersion.ToolsNetVersion != _version.ToolsNetVersion ||
                    existingVersion.JRENetVersion != _version.JRENetVersion ||
                    existingVersion.OS != _version.OS ||
                    existingVersion.Processor != _version.Processor ||
                    !Directory.Exists(_folder.IKVMJREFolder) ||
                    !Directory.Exists(_folder.IKVMToolsFolder))
                {
                    needsDownload = true;
                    _logger.LogInformation("IKVM version mismatch or missing folders detected, download required");
                }
                else
                {
                    _logger.LogInformation("IKVM already provisioned with correct version, skipping download");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read version.json, proceeding with download");
                needsDownload = true;
            }
        }
        else
        {
            needsDownload = true;
        }

        if (!needsDownload && Directory.Exists(_folder.IKVMJREFolder) && Directory.Exists(_folder.IKVMToolsFolder))
        {
            return;
        }

        var toolsUrl = BuildToolsUrl(_version.Version, _version.ToolsNetVersion, _version.OS, _version.Processor);
        var jreUrl = BuildJreUrl(_version.Version, _version.JRENetVersion, _version.OS, _version.Processor);
        var tempDirectory = _folder.TempFolder;

        var toolsArchive = Path.Combine(tempDirectory, "tools.zip");
        var jreArchive = Path.Combine(tempDirectory, "jre.zip");
        var toolsTarget = _folder.IKVMToolsFolder;
        var jreTarget = _folder.IKVMJREFolder;

        var toolsTask = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Starting download of IKVM tools from {Url}", toolsUrl);
                await DownloadAsync(toolsUrl, toolsArchive, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Starting extraction of IKVM tools to {Target}", toolsTarget);
                await ExtractArchiveAsync(toolsArchive, toolsTarget, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Successfully provisioned IKVM tools");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download or extract IKVM tools from {Url}", toolsUrl);
                throw;
            }
        }, cancellationToken);

        var jreTask = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Starting download of IKVM JRE from {Url}", jreUrl);
                await DownloadAsync(jreUrl, jreArchive, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Starting extraction of IKVM JRE to {Target}", jreTarget);
                await ExtractArchiveAsync(jreArchive, jreTarget, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Successfully provisioned IKVM JRE");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download or extract IKVM JRE from {Url}", jreUrl);
                throw;
            }
        }, cancellationToken);

        await Task.WhenAll(toolsTask, jreTask).ConfigureAwait(false);

        // Write version.json after successful download
        try
        {
            Directory.CreateDirectory(_folder.IKVMFolder);
            var versionJson = new JsonIKVMVersion
            {
                Version = _version.Version,
                ToolsNetVersion = _version.ToolsNetVersion,
                JRENetVersion = _version.JRENetVersion,
                OS = _version.OS,
                Processor = _version.Processor
            };
            var json = System.Text.Json.JsonSerializer.Serialize(versionJson, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(versionFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write version.json");
        }
    }

    /// <summary>
    /// Creates and configures an HTTP client for downloading IKVM packages.
    /// </summary>
    /// <returns>A configured <see cref="HttpClient"/> instance.</returns>
    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(IkvmCompilerDownloader));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ExtensionBridge", "1.0"));
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    /// <summary>
    /// Builds the download URL for the IKVM tools package.
    /// </summary>
    /// <param name="version">The IKVM version.</param>
    /// <param name="suffix">The .NET version suffix for the tools.</param>
    /// <param name="os">The target operating system.</param>
    /// <param name="processor">The target processor architecture.</param>
    /// <returns>The complete download URL for the IKVM tools package.</returns>
    private string BuildToolsUrl(string version, string suffix, string os, string processor)
    {
        return $"https://github.com/ikvmnet/ikvm/releases/download/{version}/IKVM-{version}-tools-{suffix}-{os}-{processor}.zip";
    }

    /// <summary>
    /// Builds the download URL for the IKVM JRE package.
    /// </summary>
    /// <param name="version">The IKVM version.</param>
    /// <param name="suffix">The .NET version suffix for the JRE.</param>
    /// <param name="os">The target operating system.</param>
    /// <param name="processor">The target processor architecture.</param>
    /// <returns>The complete download URL for the IKVM JRE package.</returns>
    private string BuildJreUrl(string version, string suffix, string os, string processor)
    {
        return $"https://github.com/ikvmnet/ikvm/releases/download/{version}/IKVM-{version}-jre-{suffix}-{os}-{processor}.zip";
    }

    /// <summary>
    /// Downloads a file from the specified URL to a local destination.
    /// </summary>
    /// <param name="url">The URL to download from.</param>
    /// <param name="destinationFile">The local file path where the download will be saved.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous download operation.</returns>
    /// <exception cref="HttpRequestException">Thrown when the HTTP request fails.</exception>
    private async Task DownloadAsync(string url, string destinationFile, CancellationToken cancellationToken)
    {
        HttpClient client = CreateHttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        await using var fileStream = File.Create(destinationFile);
        await networkStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts a ZIP archive to the specified target directory.
    /// </summary>
    /// <param name="archivePath">The path to the ZIP archive to extract.</param>
    /// <param name="targetDirectory">The directory where the archive contents will be extracted.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous extraction operation.</returns>
    /// <remarks>
    /// This method includes security checks to prevent path traversal attacks and preserves
    /// Unix file permissions when running on non-Windows platforms. If the target directory
    /// exists, it will be deleted before extraction.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an archive entry would extract outside the target directory (path traversal attack prevention).
    /// </exception>
    private static async Task ExtractArchiveAsync(string archivePath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, true);
        }

        Directory.CreateDirectory(targetDirectory);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            var fullDestinationPath = Path.GetFullPath(destinationPath);

            if (!fullDestinationPath.StartsWith(Path.GetFullPath(targetDirectory), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Archive entry would extract outside the target directory.");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullDestinationPath);
                PreserveUnixPermissions(fullDestinationPath, entry, isDirectory: true);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullDestinationPath)!);
            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(fullDestinationPath);
            await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            PreserveUnixPermissions(fullDestinationPath, entry, isDirectory: false);
        }
    }

    /// <summary>
    /// Preserves Unix file permissions from a ZIP archive entry when extracting on non-Windows platforms.
    /// </summary>
    /// <param name="path">The path to the extracted file or directory.</param>
    /// <param name="entry">The ZIP archive entry containing permission metadata.</param>
    /// <param name="isDirectory">Indicates whether the entry is a directory.</param>
    /// <remarks>
    /// This method extracts Unix file permissions from the ZIP entry's external attributes
    /// and applies them using <see cref="File.SetUnixFileMode"/>. On Windows platforms or
    /// when permissions cannot be applied, this method silently returns. This is a best-effort
    /// operation that will not throw exceptions on failure.
    /// </remarks>
    private static void PreserveUnixPermissions(string path, ZipArchiveEntry entry, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        if (mode == 0)
        {
            return;
        }

        try
        {
            var unixMode = (UnixFileMode)mode;
            if (!isDirectory)
            {
                File.SetUnixFileMode(path, unixMode);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

}

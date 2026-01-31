using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;


namespace Mihon.ExtensionsBridge.IKVMCompiler.Services;



/// <summary>
/// Provides functionality to compile JAR files to .NET assemblies using the IKVM compiler.
/// </summary>
public class IkvmCompiler : IIkvmCompiler
{
    private static readonly SemaphoreSlim _ikvmCompilerLock = new(1, 1);
    private readonly ILogger _logger;
    private readonly IIKVMVersion _version;
    private readonly ICompilerWorkingFolderStructure _folder;

    public string Version => _version.Version;

    /// <summary>
    /// Initializes a new instance of the <see cref="IkvmCompiler"/> class.
    /// </summary>
    /// <param name="version">The IKVM version configuration provider.</param>
    /// <param name="folder">The working folder structure provider.</param>
    /// <param name="logger">The logger instance for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public IkvmCompiler(IIKVMVersion version, ICompilerWorkingFolderStructure folder, ILogger<IkvmCompiler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
    }

    private string RequotePath(string path)
    {
        path = path.TrimEnd(Path.DirectorySeparatorChar);
        path = path.Trim('"');
        return $"\"{path}\"";
    }

    /// <summary>
    /// Compiles a JAR file to a .NET assembly using the IKVM compiler.
    /// </summary>
    /// <param name="entry">The repository entry containing the JAR file information and extension metadata.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous compilation operation.</returns>
    /// <exception cref="ArgumentException">Thrown when required paths or file names are invalid.</exception>
    /// <exception cref="FileNotFoundException">Thrown when required files are not found.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when required directories are not found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the IKVM compiler fails or doesn't produce the expected output.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method performs the following operations:
    /// <list type="number">
    /// <item>Validates all input paths and files</item>
    /// <item>Configures the IKVM compiler with appropriate arguments</item>
    /// <item>Executes the IKVM compiler process</item>
    /// <item>Captures and logs compiler output</item>
    /// <item>Verifies successful compilation</item>
    /// </list>
    /// The compilation is serialized using a semaphore to prevent concurrent IKVM operations.
    /// </remarks>
    public async Task CompileAsync(string jar, string versionNumber="1.0.0.0",CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jar))
            throw new ArgumentNullException(nameof(jar));
        string jarPath = Path.GetFullPath(jar);
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("JAR file not found.", jar);
       
        string ikvmTools = _folder.IKVMToolsFolder;
        string ikvmJRE = _folder.IKVMJREFolder;
        string referenceDll = _version.AndroidCompatPath;
        var resolvedJar = Path.GetFullPath(jarPath);
        var resolvedIkvmTools = Path.GetFullPath(ikvmTools);
        var resolvedIkvmJRE = Path.GetFullPath(ikvmJRE);
        var resolvedReference = Path.GetFullPath(referenceDll);
        var resolvedDestinationDir = Path.GetDirectoryName(resolvedJar);
        string finalDllPath = Path.ChangeExtension(resolvedJar, ".dll");
        if (!Directory.Exists(resolvedIkvmTools))
        {
            var exception = new DirectoryNotFoundException($"IKVM Tools directory not found at {resolvedIkvmTools}.");
            _logger.LogError(exception, "IKVM Tools directory not found at {ResolvedIkvmTools}", resolvedIkvmTools);
            throw exception;
        }
        if (!Directory.Exists(resolvedIkvmJRE))
        {
            var exception = new DirectoryNotFoundException($"IKVM JRE directory not found at {resolvedIkvmJRE}.");
            _logger.LogError(exception, "IKVM JRE directory not found at {ResolvedIkvmJRE}", resolvedIkvmJRE);
            throw exception;
        }
        if (!File.Exists(resolvedReference))
        {
            var exception = new FileNotFoundException($"Reference assembly not found at {resolvedReference}.", resolvedReference);
            _logger.LogError(exception, "Reference assembly not found at {ResolvedReference}", resolvedReference);
            throw exception;
        }
        if (!Directory.Exists(resolvedDestinationDir))
            Directory.CreateDirectory(resolvedDestinationDir);

        var ikvmCompilerPath = Path.Combine(resolvedIkvmTools, OperatingSystem.IsWindows() ? "ikvmc.exe" : "ikvmc");
        if (!File.Exists(ikvmCompilerPath))
        {
            var exception = new FileNotFoundException($"IKVM compiler not found at {ikvmCompilerPath}.", ikvmCompilerPath);
            _logger.LogError(exception, "IKVM compiler not found at {IkvmCompilerPath}", ikvmCompilerPath);
            throw exception;
        }

        if (!OperatingSystem.IsWindows())
        {
            TryEnsureExecutableBit(ikvmCompilerPath);
        }
        var assemblyName = Path.GetFileNameWithoutExtension(resolvedJar);
        var destinationDll = Path.Combine(resolvedDestinationDir, assemblyName + ".dll");
        var runtimeAssembly = Path.Combine(resolvedIkvmJRE, "bin", "IKVM.Runtime.dll");
        if (!File.Exists(runtimeAssembly))
        {
            var exception = new FileNotFoundException($"IKVM runtime assembly not found at {runtimeAssembly}.", runtimeAssembly);
            _logger.LogError(exception, "IKVM runtime assembly not found at {RuntimeAssembly}", runtimeAssembly);
            throw exception;
        }

        var currentDir = Environment.CurrentDirectory;
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        await _ikvmCompilerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = ikvmCompilerPath,
                WorkingDirectory = currentDir,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(resolvedJar);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(destinationDll);
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add(resolvedReference);
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add(Path.Combine(runtimeDirectory, "System.Runtime.dll"));
            startInfo.ArgumentList.Add("-assembly");
            startInfo.ArgumentList.Add(assemblyName);
            startInfo.ArgumentList.Add("-version");
            startInfo.ArgumentList.Add(versionNumber);
            startInfo.ArgumentList.Add("-fileversion");
            startInfo.ArgumentList.Add(versionNumber);
            startInfo.ArgumentList.Add("-lib");
            startInfo.ArgumentList.Add(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar));
            startInfo.ArgumentList.Add("-runtime");
            startInfo.ArgumentList.Add(runtimeAssembly);
            startInfo.ArgumentList.Add("-nojni");

            _logger.LogInformation("Executing IKVM compiler for assembly {AssemblyName} version {VersionNumber}", assemblyName, versionNumber);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                _logger.LogDebug("IKVM compiler stdout: {StdOut}", stdOut);
                Console.WriteLine(stdOut);
            }

            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                _logger.LogDebug("IKVM compiler stderr: {StdErr}", stdErr);
                Console.Error.WriteLine(stdErr);
            }

            if (process.ExitCode != 0)
            {
                var exception = new InvalidOperationException($"IKVM compiler exited with code {process.ExitCode}.\n\n{stdErr}");
                _logger.LogError(exception, "IKVM compiler failed with exit code {ExitCode}", process.ExitCode);
                throw exception;
            }
            if (!File.Exists(destinationDll))
            {
                var exception = new InvalidOperationException("IKVM compiler completed but did not produce the expected assembly.");
                _logger.LogError(exception, "Expected assembly not found at {DestinationDll}", destinationDll);
                throw exception;
            }
            _logger.LogInformation("IKVM compiler completed successfully with exit code {ExitCode}", process.ExitCode);
            //workUnit.Entry.Dll = await destinationDll.CalculateFileHashAsync(Version, cancellationToken).ConfigureAwait(false);

        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "IKVM compilation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IKVM compilation failed unexpectedly");
            throw;
        }
        finally
        {
            _ikvmCompilerLock.Release();
        }
        if (!File.Exists(destinationDll))
        {
            var exception = new InvalidOperationException("IKVM compiler completed but did not produce the expected assembly.");
            _logger.LogError(exception, "Expected assembly not found at {DestinationDll}", destinationDll);
            throw exception;
        }
    }

    /// <summary>
    /// Attempts to set the executable permission bit on a file for Unix-based systems.
    /// </summary>
    /// <param name="path">The file path to make executable.</param>
    /// <remarks>
    /// This method uses platform-specific APIs to set executable permissions.
    /// On .NET 6.0 or greater, it uses <see cref="File.SetUnixFileMode"/>.
    /// On earlier versions, it invokes the 'chmod' command.
    /// Any exceptions are silently caught as this is a best-effort operation.
    /// </remarks>
    private static void TryEnsureExecutableBit(string path)
    {
        try
        {
#if NET6_0_OR_GREATER
            var fileMode = File.GetUnixFileMode(path);
            fileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, fileMode);
#else
            var chmod = new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "+x", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(chmod);
            process?.WaitForExit();
#endif
        }
        catch
        {
            // best effort
        }
    }
}

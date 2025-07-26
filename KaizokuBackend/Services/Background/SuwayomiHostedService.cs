using CliWrap;
using CliWrap.EventStream;
using KaizokuBackend.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KaizokuBackend.Services.Background
{

    public class SuwayomiHostedService 
    {
        private readonly ILogger _logger;
        private readonly ILogger _slogger;
        private Command? _runningCommand;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _processTask;
        private Process? _javaProcess;
        private bool _isRunning = false;
        private readonly Lock _processLock = new Lock();

        private static readonly Regex _consoleRegex = new Regex(@"^\d+:\d+:\d+.\d+\s(.*?)\s(.*?)\s(.*)", RegexOptions.Compiled);
        private void LogLine(string line)
        {
            Match m = _consoleRegex.Match(line);
            if (m.Success){
                string section = m.Groups[1].Value;
                string level = m.Groups[2].Value;
                string message = m.Groups[3].Value;

                LogLevel logLevel = level switch
                {
                    "INFO" => LogLevel.Information,
                    "WARN" => LogLevel.Warning,
                    "ERROR" => LogLevel.Error,
                    "DEBUG" => LogLevel.Debug,
                    "CRITICAL" => LogLevel.Critical,
                    _ => LogLevel.Information
                };
                _slogger.Log(logLevel, section+" "+message);
            }
            else
            {
                _slogger.LogInformation(line);
            }
        }

        public SuwayomiHostedService(ILogger<SuwayomiHostedService> logger)
        {
            _logger = logger; //Kaz Logger
            _slogger = LoggerInfrastructure.CreateAppLogger<SuwayomiHostedService>(EnvironmentSetup.AppSuwayomi);
        }

        /// <summary>
        /// Starts the Suwayomi server by running its JAR file
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if started successfully, False otherwise</returns>
        /// <exception cref="InvalidOperationException">Thrown when the JAR file cannot be found or another error occurs</exception>
        public async Task<bool> StartAsync(string runtimeDirectory, CancellationToken cancellationToken = default)
        {
            runtimeDirectory = Path.Combine(runtimeDirectory, "Suwayomi");
            if (_isRunning)
            {
                _logger.LogInformation("Suwayomi server is already running");
                return true;
            }

            try
            {
                // Find the JAR file in the Suwayomi directory
                string? jarFile = FindJarFile(runtimeDirectory);
                if (string.IsNullOrEmpty(jarFile))
                {
                    _logger.LogError("No JAR file found in the Suwayomi directory");
                    throw new InvalidOperationException("No JAR file found in the Suwayomi directory");
                }

                string chromeLock = Path.Combine(runtimeDirectory, "cache", "kcef", "SingletonLock");
                if (File.Exists(chromeLock))
                {
                    _logger.LogInformation("Chrome lock file exists, removing it to prevent issues");
                    try
                    {
                        File.Delete(chromeLock);
                    }
                    catch (Exception )
                    {
                    }
                }

                _logger.LogInformation("Starting Suwayomi server with JAR file: {jarFile}", jarFile);
                
                // Create cancellation token source that we can use to stop the process later
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                // Use a completion source to signal when the expected text is detected
                var serverStartedTcs = new TaskCompletionSource<bool>();
                
                // Set a timeout of 1 minute (as per requirements)
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                
                // Register timeout callback
                timeoutCts.Token.Register(() => 
                {
                    if (!serverStartedTcs.Task.IsCompleted)
                    {
                        serverStartedTcs.TrySetResult(false);
                        _logger.LogError("Suwayomi server failed to start within the timeout period");
                    }
                });
                if (EnvironmentSetup.JavaRunner != "java")
                {
                    _logger.LogInformation("Running Suwayomi with {JavaRunner}.",EnvironmentSetup.JavaRunner);
                }
                string[] args = EnvironmentSetup.JavaRunner.Split(' ');
                string cmd = args[0];
                List<string> runnerArgs = [];
                if (args.Length > 1)
                {
                    runnerArgs.AddRange(args[1..]);
                }

                runnerArgs.Add($"-Dsuwayomi.tachidesk.config.server.rootDir={runtimeDirectory.Replace("\\", "/")}");
                string tmpDir = Path.Combine(runtimeDirectory, "tmp");
                if (!Directory.Exists(tmpDir))
                {
                    Directory.CreateDirectory(tmpDir);
                }
                runnerArgs.Add($"-Djava.io.tmpdir={tmpDir.Replace("\\", "/")}");

                /*
                //Added /tmp delete crontab instead

                */
                runnerArgs.Add("-jar");
                runnerArgs.Add(jarFile);
                // Start the JAR file using java with process tracking
                _runningCommand = Cli.Wrap(cmd)
                    .WithArguments(runnerArgs)
                    .WithWorkingDirectory(runtimeDirectory)
                    .WithValidation(CommandResultValidation.None);  // Don't throw on non-zero exit

                // Start the process and capture its output

                _processTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var cmdEvent in _runningCommand.ListenAsync(_cancellationTokenSource.Token).ConfigureAwait(false))

						{
                            switch (cmdEvent)
                            {
                                case StartedCommandEvent started:
                                    // Store the process reference for proper termination
                                    lock (_processLock)
                                    {
                                        _javaProcess = Process.GetProcessById(started.ProcessId);
                                        _logger.LogDebug("Suwayomi Java process started with PID: {ProcessId}", started.ProcessId);
                                    }
                                    break;

                                case StandardOutputCommandEvent stdOut:
                                    // Log the standard output
                                    LogLine(stdOut.Text);
                                    // Check for the target text that indicates the server is ready
                                    if (stdOut.Text.Contains("You are running Javalin"))
                                    {
                                        _isRunning = true;
                                        serverStartedTcs.TrySetResult(true);
                                    }
                                    break;

                                case StandardErrorCommandEvent stdErr:
                                    LogLine(stdErr.Text);
                                    break;

                                case ExitedCommandEvent exited:
                                    _isRunning = false;
                                    _logger.LogInformation("Suwayomi process exited with code {ExitCode}", exited.ExitCode);
                                    
                                    // Clear process reference
                                    lock (_processLock)
                                    {
                                        _javaProcess = null;
                                    }
                                    
                                    // If the process exited before we detected the success message, mark as failure
                                    if (!serverStartedTcs.Task.IsCompleted)
                                    {
                                        serverStartedTcs.TrySetResult(false);
                                    }
                                    break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Suwayomi process monitoring was cancelled");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error monitoring Suwayomi process");
                        if (!serverStartedTcs.Task.IsCompleted)
                        {
                            serverStartedTcs.TrySetResult(false);
                        }
                    }
                    finally
                    {
                        _isRunning = false;
                        lock (_processLock)
                        {
                            _javaProcess = null;
                        }
                    }
                }, CancellationToken.None);

                // Wait for server to start or timeout
                bool success = await serverStartedTcs.Task.ConfigureAwait(false);
                
                // If we successfully detected the server, great!
                if (success)
                {
                    _logger.LogInformation("Suwayomi server started successfully");
                    return true;
                }
                
                // If we got here without success, stop any running process and clean up
                await StopAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Suwayomi server");
                
                // Clean up if something went wrong
                try
                {
                    await StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore cleanup errors
                }
                
                throw new InvalidOperationException("Failed to start Suwayomi server", ex);
            }
        }

        /// <summary>
        /// Stops the running Suwayomi server gracefully with proper process termination
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the stop operation</param>
        /// <returns>A task representing the completion of the stop operation</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning && _javaProcess == null && _cancellationTokenSource == null)
            {
                _logger.LogInformation("Suwayomi server is not running");
                return;
            }

            try
            {
                _logger.LogInformation("Stopping Suwayomi server gracefully...");
                
                // Step 1: Cancel the monitoring task
                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }

                // Step 2: Try to terminate the Java process gracefully
                Process? processToTerminate = null;
                lock (_processLock)
                {
                    processToTerminate = _javaProcess;
                }

                if (processToTerminate != null && !processToTerminate.HasExited)
                {
                    _logger.LogInformation("Terminating Suwayomi Java process (PID: {Id})", processToTerminate.Id);
                    
                    try
                    {
                        // Try graceful shutdown first
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            // On Windows, try to close the main window first
                            processToTerminate.CloseMainWindow();
                            
                            // Wait a bit for graceful shutdown
                            bool exitedGracefully = await WaitForProcessExitAsync(processToTerminate, TimeSpan.FromSeconds(5), cancellationToken);
                            
                            if (!exitedGracefully && !processToTerminate.HasExited)
                            {
                                _logger.LogWarning("Graceful shutdown timed out, forcing termination");
                                processToTerminate.Kill(entireProcessTree: true);
                            }
                        }
                        else
                        {
                            // On Unix-like systems, send SIGTERM first
                            processToTerminate.Kill(entireProcessTree: false); // SIGTERM
                            
                            // Wait for graceful shutdown
                            bool exitedGracefully = await WaitForProcessExitAsync(processToTerminate, TimeSpan.FromSeconds(5), cancellationToken);
                            
                            if (!exitedGracefully && !processToTerminate.HasExited)
                            {
                                _logger.LogWarning("SIGTERM did not terminate process, sending SIGKILL");
                                processToTerminate.Kill(entireProcessTree: true); // SIGKILL
                            }
                        }
                        
                        // Final wait with timeout
                        await WaitForProcessExitAsync(processToTerminate, TimeSpan.FromSeconds(3), cancellationToken);
                        
                        if (processToTerminate.HasExited)
                        {
                            _logger.LogInformation("Suwayomi process terminated with exit code: {ExitCode}", processToTerminate.ExitCode);
                        }
                        else
                        {
                            _logger.LogWarning("Process did not exit after termination attempts");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited
                        _logger.LogInformation("Process had already exited");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error terminating Java process");
                    }
                }

                // Step 3: Wait for the monitoring task to complete (with timeout)
                if (_processTask != null && !_processTask.IsCompleted)
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, timeoutCts.Token);

                        await _processTask.WaitAsync(combinedCts.Token).ConfigureAwait(false);
                        _logger.LogInformation("Process monitoring task completed");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // External cancellation
                        _logger.LogInformation("Stop operation was cancelled externally");
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout
                        _logger.LogWarning("Process monitoring task did not complete within timeout");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error waiting for process monitoring task to complete");
                    }
                }

                _isRunning = false;
                _logger.LogInformation("Suwayomi server stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Suwayomi server");
            }
            finally
            {
                // Clean up resources
                try
                {
                    _cancellationTokenSource?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing cancellation token source");
                }
                
                lock (_processLock)
                {
                    _javaProcess = null;
                }
                
                _cancellationTokenSource = null;
                _runningCommand = null;
                _processTask = null;
            }
        }

        /// <summary>
        /// Waits for a process to exit with a timeout
        /// </summary>
        /// <param name="process">The process to wait for</param>
        /// <param name="timeout">Maximum time to wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if process exited within timeout, false otherwise</returns>
        private static async Task<bool> WaitForProcessExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                await process.WaitForExitAsync(combinedCts.Token);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // External cancellation
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout
                return false;
            }
        }

        /// <summary>
        /// Finds the first JAR file in the Suwayomi directory
        /// </summary>
        /// <returns>The path to the jar file, or null if not found</returns>
        private string? FindJarFile(string dir)
        {

            // Find JAR files in the directory
            var jarFiles = Directory.GetFiles(dir, "*.jar", SearchOption.TopDirectoryOnly);
            
            if (jarFiles.Length == 0)
            {
                _logger.LogWarning("No JAR files found in the directory");
                return null;
            }


            return jarFiles[0];
        }

        /// <summary>
        /// Checks if the Suwayomi server is running
        /// </summary>
        public bool IsRunning => _isRunning;
    }
}

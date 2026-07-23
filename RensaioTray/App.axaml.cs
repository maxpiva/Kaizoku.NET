using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using java.awt;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models.Abstractions;
using RensaioBackend.Utils;
using RensaioTray.Utils;
using RensaioTray.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RensaioTray;

public partial class App : Application
{
    private IHost? _host;
    private Avalonia.Controls.TrayIcon? _trayIcon;

    private IntPtr _consoleWindow = IntPtr.Zero;
    private bool _isShuttingDown = false;
    private readonly CancellationTokenSource _shutdownCancellationTokenSource = new();




    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Setup exit handlers
            desktop.Exit += async (sender, e) => await GracefulShutdownAsync();
            desktop.ShutdownRequested += (sender, e) =>
            {
                if (!_isShuttingDown)
                {
                    e.Cancel = true;
                    // GracefulShutdownAsync now disposes the tray icon, so the app can exit cleanly.
                    // If the process still refuses to exit after shutdown completes, force-exit via
                    // Environment.Exit(0) as a last resort after a timeout.
                    _ = Task.Run(async () =>
                    {
                        await GracefulShutdownAsync();
                        // Small delay to let the UI thread process the tray cleanup
                        await Task.Delay(500);
                        // Force process exit to guarantee no hang
                        Environment.Exit(0);
                    });
                }
            };

            // Check if storage folder exists
            bool rootFolderExists = EnvironmentSetup.CheckIfRootDirExists();
            
            if (!rootFolderExists)
            {
                // Show StorageFolderDialog and wait for result
                ShowStorageFolderDialogAndWait(desktop);
            }
            else
            {
                // Storage folder exists, start normally
                Task.Run(async () =>
                {
                    await EnvironmentSetup.InitializeAsync();
                    await StartApplication();
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowStorageFolderDialogAndWait(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dialog = new StorageFolderDialog();
        desktop.MainWindow = dialog;
        
        dialog.Closed += async (sender, e) =>
        {
            var result = dialog.DialogResult ? dialog.SelectedFolderPath : null;
            
            if (!string.IsNullOrEmpty(result) && Directory.Exists(result))
            {
                try
                {

                    
                    // Hide the dialog window and start the app
                    desktop.MainWindow = null;

                    // Start the application on a background thread

                    // Initialize environment with selected folder
                    await EnvironmentSetup.InitializeAsync(result);
                    await StartApplication();
  
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize with selected folder: {ex.Message}");
                    desktop.Shutdown();
                }
            }
            else
            {
                // User cancelled or selected invalid folder
                Console.WriteLine("No valid storage folder selected. Application will exit.");
                desktop.Shutdown();
            }
        };
    }

    private async Task StartApplication()
    {
        try
        {
            // Build and start the ASP.NET Core host (this can run on background thread)
            _host = RensaioBackend.Program.CreateHostBuilder(Array.Empty<string>()).AddCefTimer().Build();
            // Start the ASP.NET Core host (this can run on background thread).
            // Wrap in a tracked task so host crashes are logged even though we
            // deliberately don't await here (to keep the UI thread responsive).
            _ = Task.Run(async () =>
            {
                try
                {
                    await _host.StartAsync(_shutdownCancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation during shutdown — no action needed.
                }
                catch (Exception ex)
                {
                    FallbackCrashLogger.WriteException(ex, "TRAY HOST STARTAsync FAILED");
                    Console.WriteLine($"Backend host failed to start: {ex.Message}");
                }
            });
            // Setup the tray icon on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                SetupTrayIcon();
            });
        }
        catch (Exception ex)
        {
            FallbackCrashLogger.WriteException(ex, "TRAY StartApplication FAILED");
            Console.WriteLine($"Failed to start application: {ex.Message}");
        }
    }


    

    private async Task GracefulShutdownAsync()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;

        try
        {
            if (_host != null)
            {
                // 1) Stop the host with a 30-second timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdownCancellationTokenSource.Token, timeoutCts.Token);

                try
                {
                    await _host.StopAsync(combinedCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow shutdown exceptions
                }

                // 2) Stop the CEF message pump timer before disposing the host

                // 3) Dispose the host — use ConfigureAwait(false) to avoid UI thread deadlock.
                // IHost.Dispose() internally calls DisposeAsync().GetAwaiter().GetResult()
                // which can deadlock on Avalonia's UI synchronization context.
                await Task.Run(() => _host.Dispose()).ConfigureAwait(false);
            }

            // 4) Clean up UI resources on the UI thread to let the event loop exit
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_trayIcon != null)
                {
                    // Windows / Linux: dispose the Avalonia TrayIcon
                    _trayIcon.IsVisible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            });
        }
        finally
        {
            _shutdownCancellationTokenSource.Dispose();
        }
    }

    private void SetupTrayIcon()
    {
        try
        {



            // ── Windows / Linux: use Avalonia's TrayIcon with NativeMenu ─
            var trayIcon = new Avalonia.Controls.TrayIcon();

            try
            {
                var uri = new Uri("avares://RensaioTray/Assets/rensaio.ico");
                using var stream = AssetLoader.Open(uri);
                Bitmap bitmap = new Bitmap(stream);
                var icon = new WindowIcon(bitmap);
                trayIcon.Icon = icon;
            }
            catch
            {
                Console.WriteLine("Could not load tray icon from resources, using default icon");
            }

            trayIcon.ToolTipText = "Rensaiō";

            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Open App in the Browser");
            openItem.Click += OpenItem_Click;

            var consoleItem = new NativeMenuItem("Show Console");
            consoleItem.Click += ConsoleItem_Click;

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += async (sender, args) =>
            {
                await GracefulShutdownAsync();
                // Force process exit — the backend is fully stopped at this point
                Environment.Exit(0);
            };

            menu.Add(openItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(consoleItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            trayIcon.Menu = menu;
            trayIcon.IsVisible = true;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NativeDock.SetMenu(this, menu);
            }
            _trayIcon = trayIcon;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to setup tray icon: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the Rensaiō web interface in the default browser (macOS helper).
    /// </summary>
    private void OpenAppInBrowser()
    {
        try
        {
            var server = _host?.Services.GetService<IServer>();
            var addresses = server?.Features.Get<IServerAddressesFeature>();
            var address = addresses?.Addresses.FirstOrDefault();

            if (address != null)
            {
                var url = address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost");
                Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open web interface: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a terminal window (macOS helper).
    /// </summary>
    private void ConsoleItem_Click_Mac()
    {
        ShowConsoleAlternative();
    }

    /// <summary>
    /// Gracefully shuts down and exits the application (macOS helper).
    /// </summary>
    private async void ExitApplication()
    {
        await GracefulShutdownAsync();
        Environment.Exit(0);
    }


    private void ConsoleItem_Click(object? sender, EventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ToggleConsoleVisibility();
        }
        else
        {
            ShowConsoleAlternative();
        }
    }

    private void ToggleConsoleVisibility()
    {
        if (_consoleWindow == IntPtr.Zero)
        {
            _consoleWindow = ConsoleUtils.GetConsoleWindow();
        }

        if (_consoleWindow != IntPtr.Zero)
        {
            // Use IsWindowVisible to check the actual state of the window
            bool isVisible = AnsiConsoleUtils.IsConsoleWindowVisible();

            if (isVisible)
            {
                // Hide the console
                ConsoleUtils.ShowWindow(_consoleWindow, ConsoleUtils.SW_HIDE);
                UpdateConsoleMenuItem("Show Console");
            }
            else
            {
                // Show the console
                ConsoleUtils.ShowWindow(_consoleWindow, ConsoleUtils.SW_SHOW);
                UpdateConsoleMenuItem("Hide Console");
            }
        }
        else
        {
            Console.WriteLine("Could not get console window handle.");
        }
    }

    private void ShowConsoleAlternative()
    {
        try
        {
            var logDir = System.IO.Path.Combine(EnvironmentSetup.Path, "logs");
            Console.WriteLine($"[ShowConsoleAlternative] logDir resolved to: {logDir}");
            string? latestLogPath = null;

            if (System.IO.Directory.Exists(logDir))
            {
                Console.WriteLine($"[ShowConsoleAlternative] logDir exists, scanning for log files...");
                var latestLog = new System.IO.DirectoryInfo(logDir)
                    .GetFiles("log-*.txt")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                if (latestLog != null)
                {
                    latestLogPath = latestLog.FullName;
                    Console.WriteLine($"[ShowConsoleAlternative] Found latest log: {latestLogPath}");
                }
                else
                {
                    Console.WriteLine($"[ShowConsoleAlternative] No log-*.txt files found in {logDir}");
                }
            }
            else
            {
                Console.WriteLine($"[ShowConsoleAlternative] logDir does not exist: {logDir}");
            }

            Console.WriteLine($"[ShowConsoleAlternative] Platform: {RuntimeInformation.OSDescription}, latestLogPath: {latestLogPath ?? "null"}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var logPath = latestLogPath ?? "logs/log-.txt";
                Console.WriteLine($"[ShowConsoleAlternative] Linux: starting x-terminal-emulator with tail -f '{logPath}'");
                Process.Start("x-terminal-emulator", $"-e tail -f '{logPath}'");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (latestLogPath != null)
                {
                    // Use ProcessStartInfo with ArgumentList to avoid shell argument splitting issues
                    Console.WriteLine($"[ShowConsoleAlternative] macOS: starting osascript with tail -f on {latestLogPath}");
                    var psi = new ProcessStartInfo("osascript")
                    {
                        ArgumentList = { "-e", $"tell app \"Terminal\" to do script \"tail -f '{latestLogPath}'\"" }
                    };
                    Process.Start(psi);
                }
                else if (System.IO.Directory.Exists(logDir))
                {
                    Console.WriteLine($"[ShowConsoleAlternative] macOS fallback: opening Terminal at {logDir}");
                    // Use ArgumentList to handle paths with spaces correctly
                    var psi = new ProcessStartInfo("open")
                    {
                        ArgumentList = { "-a", "Terminal", logDir }
                    };
                    Process.Start(psi);
                }
                else
                {
                    Console.WriteLine($"[ShowConsoleAlternative] macOS last resort: opening blank Terminal");
                    Process.Start("open", "-a Terminal -n");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ShowConsoleAlternative] ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void UpdateConsoleMenuItem(string text)
    {
        if (_trayIcon?.Menu != null)
        {
            foreach (var item in _trayIcon.Menu.Items)
            {
                if (item is NativeMenuItem menuItem &&
                    (menuItem.Header == "Show Console" || menuItem.Header == "Hide Console"))
                {
                    menuItem.Header = text;
                    break;
                }
            }
        }
    }

    private void OpenItem_Click(object? sender, EventArgs e)
    {
        try
        {
            var server = _host?.Services.GetService<IServer>();
            var addresses = server?.Features.Get<IServerAddressesFeature>();
            var address = addresses?.Addresses.FirstOrDefault();

            if (address != null)
            {
                var url = address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open web interface: {ex.Message}");
        }
    }
}
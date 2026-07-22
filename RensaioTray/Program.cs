using Avalonia;
using RensaioBackend.Utils;
using RensaioTray.Utils;
using Serilog;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RensaioTray;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("IKVM_VERBOSE", "1");

        // Initialize the zero-dependency crash logger as early as possible.
        // EnvironmentSetup.Path is resolved in the static constructor, so it
        // is safe to use here even before InitializeAsync() is called.
        var crashLogDir = System.IO.Path.Combine(EnvironmentSetup.Path, "logs");
        FallbackCrashLogger.Initialize(crashLogDir);

       
        // Install the Windows Vectored Exception Handler (VEH) to catch native
        // crashes (AccessViolation, StackOverflow, etc.) that managed handlers
        // can never see.  This is CRITICAL because IKVM runs native JVM code
        // that can AV without any managed exception handler being invoked.
        /*
        NativeCrashHandler.SetLogPath(System.IO.Path.Combine(crashLogDir, "crash-.log"));
        NativeCrashHandler.Install();
        */
        // Register FIRST-CHANCE exception handler to catch exceptions that are
        // thrown and caught internally — these never reach UnhandledException.
        AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
        {
            var source = e.Exception?.Source ?? "";
            if (source.Contains("IKVM") || source.Contains("ikvm") ||
                source.Contains("Android") || source.Contains("Mihon") ||
                source.Contains("Rensaio") || source.Contains("Java"))
            {
                // Skip exceptions the Java side throws-and-catches as normal control
                // flow. dex2jar alone raised ~20k MergeResult exceptions during the
                // post-update extension recompile, flooding the crash log with
                // hundreds of thousands of lines of synchronous file I/O.
                var typeName = e.Exception?.GetType().FullName ?? "";
                if (typeName.StartsWith("com.googlecode.dex2jar.") ||
                    typeName == "java.lang.ClassNotFoundException" ||
                    typeName == "java.lang.NoSuchFieldException" ||
                    typeName == "java.lang.NoSuchMethodException")
                {
                    return;
                }

                FallbackCrashLogger.WriteException(e.Exception,
                    "TRAY FIRSTCHANCE: " + e.Exception?.GetType().Name + " from " + source);
            }
        };

        // Register global exception traps BEFORE any application code runs.
        // These handlers capture crashes that bypass Avalonia's and
        // AspNetCore's error pipelines (native crashes, StackOverflowException,
        // finalizer thread crashes, unobserved task exceptions, silent kills).
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            FallbackCrashLogger.WriteException(ex,
                "TRAY APPDOMAIN UNHANDLED EXCEPTION (terminating=" + e.IsTerminating + ")");
            try { Log.Fatal(ex, "TRAY APPDOMAIN UNHANDLED EXCEPTION (terminating={IsTerminating})", e.IsTerminating); } catch { }
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            FallbackCrashLogger.WriteException(e.Exception, "TRAY UNOBSERVED TASK EXCEPTION");
            try { Log.Fatal(e.Exception, "TRAY UNOBSERVED TASK EXCEPTION"); } catch { }
            e.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            var state = "unknown";
            try { state = Environment.HasShutdownStarted ? "shutdown-started" : "normal"; } catch { }
            FallbackCrashLogger.Write("TRAY ProcessExit fired (HasShutdownStarted=" + state + ")");
            try { Log.Information("TRAY ProcessExit fired (HasShutdownStarted={State})", state); } catch { }
           /* try { NativeCrashHandler.Uninstall(); } catch { }*/
            try { Log.CloseAndFlush(); } catch { }
        };

        java.lang.System.setProperty("java.awt.headless", "true");
        if (!EnvironmentSetup.IsApplicationAlreadyRunning())
        {
            try
            {
                // On Windows, set up the console before doing anything else.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    InitializeConsole();
                }
                
                // Build and run the Avalonia application.
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception ex)
            {
                FallbackCrashLogger.WriteException(ex, "TRAY AVALONIA STARTUP FAILED");
                // Log any critical startup errors.
                Console.WriteLine($"Application startup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Allocates a console, sets its icon, disables the close button, and hides it.
    /// </summary>
    private static void InitializeConsole()
    {
        // Allocate a new console window for the application.
        if (ConsoleUtils.AllocConsole())
        {
            // Set the console icon
            if (!ConsoleUtils.SetConsoleIcon())
            {
                Console.WriteLine("Warning: Failed to set console icon.");
            }

            // Disable the close button on the console window.
            if (!ConsoleUtils.DisableConsoleCloseButton())
            {
                Console.WriteLine("Warning: Failed to disable the console close button. The console may be closeable by the user.");
            }

            // Hide the console window initially. It can be shown later by the application logic.
            IntPtr consoleWindow = ConsoleUtils.GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                ConsoleUtils.ShowWindow(consoleWindow, ConsoleUtils.SW_HIDE);
            }
        }
        else
        {
            Console.WriteLine("Could not allocate a new console.");
        }
    }

    /// <summary>
    /// Configures and builds the Avalonia application.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

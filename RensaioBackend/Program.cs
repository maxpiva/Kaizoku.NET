using RensaioBackend.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace RensaioBackend
{
    public static class Program
    {

        public static async Task Main(string[] args)
        {
            // Initialize the zero-dependency fallback crash logger BEFORE registering
            // global handlers.  EnvironmentSetup.Path is resolved in the static
            // constructor so it's safe to use here.  This ensures crash-(date).log
            // is available from the very first millisecond of the process.
            var crashLogDir = System.IO.Path.Combine(EnvironmentSetup.Path, "logs");
            FallbackCrashLogger.Initialize(crashLogDir);

            // Install the Windows Vectored Exception Handler (VEH) to catch native
            // crashes (AccessViolation, StackOverflow, etc.) that managed handlers
            // can never see.  This is CRITICAL because IKVM runs native JVM code
            // that can AV without any managed exception handler being invoked.
            NativeCrashHandler.SetLogPath(System.IO.Path.Combine(crashLogDir, "crash-.log"));
            NativeCrashHandler.Install();

            // Register FIRST-CHANCE exception handler to catch exceptions that are
            // thrown and caught internally — these never reach UnhandledException.
            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                // Log only unexpected internal exceptions, not normal framework patterns.
                // We filter by source to avoid noise from IL weaving, EF, etc.
                var source = e.Exception?.Source ?? "";
                if (source.Contains("IKVM") || source.Contains("ikvm") ||
                    source.Contains("Android") || source.Contains("Mihon") ||
                    source.Contains("Rensaio") || source.Contains("Java"))
                {
                    FallbackCrashLogger.WriteException(e.Exception,
                        "FIRSTCHANCE: " + e.Exception?.GetType().Name + " from " + source);
                }
            };

            // Register global exception traps BEFORE any initialization code runs.
            // These handlers capture crashes that bypass AspNetCore's error pipeline
            // (e.g. native crashes, StackOverflowException, finalizer thread crashes,
            // unobserved task exceptions, and silent process kills).
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                FallbackCrashLogger.WriteException(ex, "APPDOMAIN UNHANDLED EXCEPTION (terminating=" + e.IsTerminating + ")");
                // Also try Serilog in case it's still healthy
                try { Log.Fatal(ex, "APPDOMAIN UNHANDLED EXCEPTION (terminating={IsTerminating})", e.IsTerminating); } catch { }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                FallbackCrashLogger.WriteException(e.Exception, "UNOBSERVED TASK EXCEPTION");
                try { Log.Fatal(e.Exception, "UNOBSERVED TASK EXCEPTION"); } catch { }
                e.SetObserved();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                // Log process exit with reason context when possible.
                // This helps distinguish between intentional shutdown and involuntary termination.
                var state = "unknown";
                try { state = Environment.HasShutdownStarted ? "shutdown-started" : "normal"; } catch { }
                FallbackCrashLogger.Write("ProcessExit fired (HasShutdownStarted=" + state + ")");
                try { Log.Information("ProcessExit fired (HasShutdownStarted={State})", state); } catch { }
                // Uninstall VEH to avoid native handler running after we've started cleanup
                try { NativeCrashHandler.Uninstall(); } catch { }
                // Flush Serilog synchronously so buffered entries are written before the process dies.
                try { Log.CloseAndFlush(); } catch { }
            };

            await EnvironmentSetup.InitializeAsync(null);

            var host = CreateHostBuilder(args).Build();

            try
            {
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                FallbackCrashLogger.WriteException(ex, "HOST RUN THREW UNEXPECTED EXCEPTION");
                try { Log.Fatal(ex, "Host terminated unexpectedly"); } catch { }
                throw;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {

            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseWebRoot(Path.Combine(EnvironmentSetup.Configuration!["runtimeDirectory"]!, "wwwroot"));
                    webBuilder.ConfigureAppConfiguration(AppConfiguration);
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(server =>
                    {
                        var config = EnvironmentSetup.Configuration!;
                        var port = config.GetValue<int>(
#if DEBUG
                            "Kestrel:Ports:Debug"
#else
    "Kestrel:Ports:Release"
#endif
                            , 5005);
                        EnvironmentSetup.Logger.LogInformation("Starting Rensaiō on port {port}...", port);
                        server.ListenAnyIP(port);
                    });
                });
        }

        private static void AppConfiguration(WebHostBuilderContext context, IConfigurationBuilder builder)
        {
            EnvironmentSetup.AddConfigurations(builder);
        }
    }
}
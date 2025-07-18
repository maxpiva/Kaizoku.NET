using Serilog;
using Serilog.Filters;
using Serilog.Sinks.SystemConsole.Themes;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KaizokuBackend.Utils
{
    public static class LoggerInfrastructure
    {
        public static void BuildLogger(IConfiguration iconfig)
        {
            string current = iconfig.GetValue<string>("Serilog:WriteTo:0:Args:outputTemplate", "[{App}][{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
            string kaiz = current.Replace("[{App}]", "\u001b[32m[Kaiz.NET]\u001b[0m");
            string suwa = current.Replace("[{App}]", "\u001b[34m[Suwayomi]\u001b[0m");

            Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(iconfig)
                .Enrich.WithProperty("App", "Kaiz.NET")
                .WriteTo.Logger(lc =>
                {
                    lc.Filter.ByIncludingOnly(Matching.WithProperty("App", "Kaiz.NET"))
                        .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: kaiz, applyThemeToRedirectedOutput: true);

                })
                .WriteTo.Logger(lc =>
                {
                    lc.Filter.ByIncludingOnly(Matching.WithProperty("App", "Suwayomi"))
                        .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: suwa, applyThemeToRedirectedOutput: true);
                })
                .Enrich.FromLogContext().CreateLogger();
        }

        /// <summary>
        /// Checks if a console window is available for output
        /// </summary>
        /// <returns>True if console is available, false otherwise</returns>
        private static bool HasConsoleWindow()
        {
            try
            {
                // Try to get console window handle
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    return GetConsoleWindow() != IntPtr.Zero;
                }
                else
                {
                    // For non-Windows platforms, check if we have console access
                    return !Console.IsOutputRedirected;
                }
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        /// <summary>
        /// Reconfigures logger to include console output when console window becomes available
        /// </summary>
        public static void EnableConsoleLogging(IConfiguration iconfig)
        {
            // Rebuild logger configuration with console output enabled
            BuildLogger(iconfig);
        }

        public static ILogger<T> CreateAppLogger<T>(string app)
        {
            ILoggerFactory lfac = LoggerFactory.Create(builder =>
            {
                var logger = Log.Logger.ForContext("App", app);
                builder.AddSerilog(logger);
            });
            return lfac.CreateLogger<T>();
        }

        public static ILogger CreateAppLogger(string app, string cls)
        {
            ILoggerFactory lfac = LoggerFactory.Create(builder =>
            {
                var logger = Log.Logger.ForContext("App", app);
                builder.AddSerilog(logger);
            });
            return lfac.CreateLogger(cls);
        }
    }
}

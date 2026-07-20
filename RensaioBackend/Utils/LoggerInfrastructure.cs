using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Parsing;
using Serilog.Sinks.SystemConsole.Themes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace RensaioBackend.Utils
{
    /// <summary>
    /// Registers a Windows Vectored Exception Handler (VEH) that intercepts
    /// native/SEH exceptions (e.g. AccessViolation, StackOverflow) before the
    /// OS terminates the process.  Managed handlers like AppDomain.UnhandledException
    /// are NEVER invoked for these crashes, so VEH is the only way to log them.
    ///
    /// Must be called from the process entry point before any other code runs.
    /// Safe to call on non-Windows platforms — the P/Invoke is skipped.
    /// </summary>
    /// <summary>
    /// Cross-platform native crash and signal handler.
    ///
    /// Windows: Installs a Vectored Exception Handler (VEH) to intercept
    ///   native SEH exceptions (AccessViolation, StackOverflow, etc.) that
    ///   managed handlers can never see.
    /// Linux/macOS: Hooks SIGTERM, SIGINT, SIGQUIT via PosixSignal.
    ///   SIGSEGV/SIGABRT cannot be safely intercepted from managed code on
    ///   these platforms — only the managed handlers can catch those.
    ///
    /// Safe to call on all platforms — the platform-specific code is
    /// guarded by runtime checks.
    /// </summary>
    public static class NativeCrashHandler
    {
        // ---- Windows VEH ----
        private const uint EXCEPTION_CONTINUE_SEARCH = 0;
        private const uint EXCEPTION_ACCESS_VIOLATION = 0xC0000005;
        private const uint EXCEPTION_STACK_OVERFLOW = 0xC00000FD;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr AddVectoredExceptionHandler(uint first, IntPtr handler);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool RemoveVectoredExceptionHandler(IntPtr handler);

        private delegate uint VectoredExceptionHandlerDelegate(IntPtr exceptionPointers);
        private static VectoredExceptionHandlerDelegate? _vehDelegate;
        private static IntPtr _vehHandle = IntPtr.Zero;

        // ---- Posix signals (cross-platform) ----
        private static List<PosixSignalRegistration>? _posixRegistrations;

        // ---- Shared state ----
        private static string? _logPath;

        public static void SetLogPath(string path)
        {
            _logPath = path;
        }

        /// <summary>
        /// Installs platform-specific native crash handlers.
        /// Windows: VEH.  Linux/macOS: PosixSignal handlers.
        /// </summary>
        public static void Install()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                InstallWindowsVeh();
            }
            else
            {
                InstallPosixHandlers();
            }
        }

        /// <summary>
        /// Uninstalls all native handlers.
        /// </summary>
        public static void Uninstall()
        {
            UninstallWindowsVeh();
            UninstallPosixHandlers();
        }

        // ---- Windows VEH implementation ----

        private static void InstallWindowsVeh()
        {
            try
            {
                _vehDelegate = VectoredHandler;
                var fp = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_vehDelegate);
                _vehHandle = AddVectoredExceptionHandler(1, fp);
                if (_vehHandle == IntPtr.Zero)
                    _vehDelegate = null;
            }
            catch
            {
                _vehDelegate = null;
            }
        }

        private static void UninstallWindowsVeh()
        {
            if (_vehHandle != IntPtr.Zero)
            {
                try { RemoveVectoredExceptionHandler(_vehHandle); } catch { }
                _vehHandle = IntPtr.Zero;
                _vehDelegate = null;
            }
        }

        private static uint VectoredHandler(IntPtr exceptionPointers)
        {
            uint code = 0;
            try { code = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(exceptionPointers, 0); }
            catch { return EXCEPTION_CONTINUE_SEARCH; }

            // Skip debug events
            if (code == 0x40010006 || code == 0x4001000A)
                return EXCEPTION_CONTINUE_SEARCH;

            ulong address = 0;
            try
            {
                var addrPtr = exceptionPointers + 8;
                address = IntPtr.Size == 8
                    ? (ulong)System.Runtime.InteropServices.Marshal.ReadInt64(addrPtr)
                    : (ulong)System.Runtime.InteropServices.Marshal.ReadInt32(addrPtr);
            }
            catch { }

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var codeName = code switch
            {
                EXCEPTION_ACCESS_VIOLATION => "ACCESS_VIOLATION",
                EXCEPTION_STACK_OVERFLOW => "STACK_OVERFLOW",
                0xC000001D => "ILLEGAL_INSTRUCTION",
                0xC0000006 => "IN_PAGE_ERROR",
                0xC0000094 => "INT_DIVIDE_BY_ZERO",
                0xC0000096 => "PRIV_INSTRUCTION",
                0xE06D7363 => "CPP_EXCEPTION",
                _ => "UNKNOWN"
            };

            string detail = "";
            if (code == EXCEPTION_ACCESS_VIOLATION)
            {
                try
                {
                    int infoOffset = 28;
                    var flags = System.Runtime.InteropServices.Marshal.ReadInt32(exceptionPointers, infoOffset);
                    var accessType = (flags & 0xFF) switch
                    {
                        0 => "READ", 1 => "WRITE", 8 => "EXECUTE",
                        _ => $"flags=0x{flags:X}"
                    };
                    var violAddr = (ulong)System.Runtime.InteropServices.Marshal.ReadInt64(exceptionPointers, infoOffset + 8);
                    detail = $" ({accessType} at 0x{violAddr:X16})";
                }
                catch { }
            }

            WriteToCrashLog($"[{timestamp}] [VEH] NATIVE EXCEPTION: 0x{code:X8} ({codeName}) at 0x{address:X16}{detail}");

            return EXCEPTION_CONTINUE_SEARCH;
        }

        // ---- Posix signal handlers (Linux/macOS) ----

        private static void InstallPosixHandlers()
        {
            try
            {
                _posixRegistrations = new List<PosixSignalRegistration>(3);

                // SIGTERM — graceful shutdown requested
                _posixRegistrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    WriteToCrashLog($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [POSIX] SIGTERM received — process terminating");
                    ctx.Cancel = false; // Allow default behavior
                }));

                // SIGINT — Ctrl+C
                _posixRegistrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
                {
                    WriteToCrashLog($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [POSIX] SIGINT received");
                    ctx.Cancel = false;
                }));

                // SIGQUIT — Ctrl+\ (also triggers core dump on Linux)
                try
                {
                    _posixRegistrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGQUIT, ctx =>
                    {
                        WriteToCrashLog($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [POSIX] SIGQUIT received — process terminating");
                        ctx.Cancel = false;
                    }));
                }
                catch
                {
                    // SIGQUIT may not be available on all platforms
                }
            }
            catch
            {
                // PosixSignal not supported on this platform
            }
        }

        private static void UninstallPosixHandlers()
        {
            if (_posixRegistrations != null)
            {
                foreach (var reg in _posixRegistrations)
                {
                    try { reg.Dispose(); } catch { }
                }
                _posixRegistrations = null;
            }
        }

        // ---- Shared log writer ----

        private static void WriteToCrashLog(string message)
        {
            if (_logPath == null) return;
            try
            {
                message += Environment.NewLine;
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(message);
                using var fs = new System.IO.FileStream(_logPath,
                    System.IO.FileMode.Append, System.IO.FileAccess.Write,
                    System.IO.FileShare.ReadWrite, 4096, useAsync: false);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush();
            }
            catch { }
        }
    }

    /// <summary>
    /// Last-resort crash logger that writes to a file using only raw I/O,
    /// with zero dependency on Serilog or any other framework.
    /// Use when the process is about to die from an unhandled exception
    /// and Serilog may no longer be safe to call.
    /// </summary>
    public static class FallbackCrashLogger
    {
        private static readonly object _lock = new();
        private static string? _logPath;

        /// <summary>
        /// Initializes the fallback crash logger with the directory where crash logs will be written.
        /// Must be called once as early as possible (before any exception handler can fire).
        /// Safe to call multiple times — only the first call takes effect.
        /// </summary>
        public static void Initialize(string logsDirectory)
        {
            if (_logPath != null)
                return;
            try
            {
                if (!Directory.Exists(logsDirectory))
                    Directory.CreateDirectory(logsDirectory);
                _logPath = Path.Combine(logsDirectory, "crash-.log");
                // Write a header to confirm the file is writable
                var header = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] FallbackCrashLogger initialized.{Environment.NewLine}";
                File.AppendAllText(_logPath, header);
            }
            catch
            {
                // Nothing we can do — logging is unavailable
            }
        }

        /// <summary>
        /// Writes a single line to the crash log with zero-throw guarantee.
        /// Safe to call from unhandled-exception handlers, OOM scenarios, etc.
        /// </summary>
        public static void Write(string message, [CallerMemberName] string caller = "")
        {
            if (_logPath == null)
                return;
            try
            {
                var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{caller}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // Absolute last resort — swallow silently
            }
        }

        /// <summary>
        /// Writes exception details to the crash log with zero-throw guarantee.
        /// </summary>
        public static void WriteException(Exception? ex, string context, [CallerMemberName] string caller = "")
        {
            if (_logPath == null)
                return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{caller}] CRASH: {context}");
                if (ex != null)
                {
                    sb.AppendLine($"  Type:     {ex.GetType().FullName}");
                    sb.AppendLine($"  Message:  {ex.Message}");
                    sb.AppendLine($"  HResult:  0x{ex.HResult:X8}");
                    sb.AppendLine($"  StackTrace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"  InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                        sb.AppendLine($"  InnerStackTrace: {ex.InnerException.StackTrace}");
                    }
                    sb.AppendLine($"  Source:   {ex.Source}");
                    sb.AppendLine($"  TargetSite: {ex.TargetSite}");
                }
                sb.AppendLine("--- end crash entry ---");
                lock (_lock)
                {
                    File.AppendAllText(_logPath, sb.ToString());
                }
            }
            catch
            {
                // Nothing more we can do
            }
        }
    }

    public static class LoggerInfrastructure
    {
        public static int PascalClassNameWidth = 0;
        private static readonly (string App, string Colored)[] ConsoleAppStyles =
        [
            (EnvironmentSetup.AppRensaio, "\u001b[32mRensaiō\u001b[0m"),
            (EnvironmentSetup.AppMihon, "\u001b[34mMihonEx\u001b[0m"),
            (EnvironmentSetup.AppAndroid, "\u001b[36mAndroid\u001b[0m")
        ];
        
        private static readonly Regex ClassWidthRegex =
            new(@"\{PascalClassName[^,}]*,\s*(-?\d+)\}", RegexOptions.Compiled);
        private static readonly Regex PascalClassNameRegex =
            new(@"\{PascalClassName\}", RegexOptions.Compiled);

        public static int GetClassColumnWidth(string outputTemplate, int fallback = 20)
        {
            if (string.IsNullOrWhiteSpace(outputTemplate))
                return -1;
            var match = PascalClassNameRegex.Match(outputTemplate);
            if (match.Success)
                return fallback; // default width when no explicit width is set
            match = ClassWidthRegex.Match(outputTemplate);
            if (!match.Success)
                return -1;

            if (int.TryParse(match.Groups[1].Value, out int width))
                return Math.Abs(width); // -20 or 20 → we only care about width

            return fallback;
        }


        public static void BuildLogger(IConfiguration iconfig)
        {
            string current = iconfig.GetValue<string>("Serilog:WriteTo:0:Args:outputTemplate", "[{App} {Timestamp:HH:mm:ss} {Level:u3} {PascalClassName,-20}] {Message:lj}{NewLine}{Exception}");
            PascalClassNameWidth = GetClassColumnWidth(current, 20);

            var sinkConfiguration = new LoggerConfiguration()
                .ReadFrom.Configuration(iconfig);

            foreach (var (app, coloredName) in ConsoleAppStyles)
            {
                string outputTemplate = current.Replace("{App}", coloredName);
                AddColoredConsoleSink(sinkConfiguration, app, outputTemplate);
            }

            var innerLogger = sinkConfiguration.CreateLogger();

            var outerConfiguration = new LoggerConfiguration();
            ConfigureMinimumLevels(outerConfiguration, iconfig);

            Log.Logger = outerConfiguration
                .Enrich.WithProperty("App", EnvironmentSetup.AppRensaio)
                .Enrich.FromLogContext()
                .WriteTo.Sink(new RewriteMessageSink(new LoggerAsSink(innerLogger)))
                .CreateLogger();
        }

        private static void ConfigureMinimumLevels(LoggerConfiguration config, IConfiguration iconfig)
        {
            var minimumLevelSection = iconfig.GetSection("Serilog").GetSection("MinimumLevel");
            if (!minimumLevelSection.Exists())
                return;

            var defaultLevel = minimumLevelSection["Default"];
            if (!string.IsNullOrWhiteSpace(defaultLevel) && Enum.TryParse(defaultLevel, true, out LogEventLevel defaultLogLevel))
            {
                config.MinimumLevel.Is(defaultLogLevel);
            }

            var overrideSection = minimumLevelSection.GetSection("Override");
            foreach (var child in overrideSection.GetChildren())
            {
                if (Enum.TryParse(child.Value, true, out LogEventLevel level))
                {
                    config.MinimumLevel.Override(child.Key, level);
                }
            }
        }

        private static void AddColoredConsoleSink(LoggerConfiguration sinkConfiguration, string app, string template)
        {
            sinkConfiguration.WriteTo.Logger(lc =>
            {
                var console = new LoggerConfiguration()
                    .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: template, applyThemeToRedirectedOutput: true)
                    .CreateLogger();

                lc.Filter.ByIncludingOnly(Matching.WithProperty("App", app))
                  .WriteTo.Sink(new LoggerAsSink(console));
            });
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

    /*
    public sealed class RewriteMessageSink : ILogEventSink, IDisposable
    {
        private static readonly Regex Prefix =
            new(@"^\[(?<class>[^\]]+)\]\s*", RegexOptions.Compiled);

        private readonly ILogEventSink _inner;
        private readonly IDisposable? _innerDispose;
        private readonly MessageTemplateParser _parser = new();
        public RewriteMessageSink(ILogEventSink inner)
        {
            _inner = inner;
            _innerDispose = inner as IDisposable;
        }

        public void Emit(LogEvent logEvent)
        {
            bool change = false;
            var props = new List<LogEventProperty>();
            if (!logEvent.Properties.TryGetValue("App", out var appScalar))
            {
                _inner.Emit(logEvent);
                return;
            }
            if (!(appScalar is ScalarValue { Value: string app }))
            {
                _inner.Emit(logEvent);
                return;
            }



            if (logEvent.Properties.TryGetValue("SourceContext", out var sc))
            {
                if (sc is ScalarValue { Value: string fullName })
                {
                    if (app!="Android")
                    {
                        var className2 = fullName.Split('.').Last();
                        props.Add(new LogEventProperty("ClassName", new ScalarValue(className2)));
                        change = true;
                    }
                }
            }
            var text = logEvent.MessageTemplate.Text;
            var newText = text;
            if (!change && app=="Android")
            {
                if (!logEvent.Properties.TryGetValue("AndroidCompatMessage", out var andr))
                {
                    _inner.Emit(logEvent);
                    return;
                }
                if (!(andr is ScalarValue { Value: string android }))
                {
                    _inner.Emit(logEvent);
                    return;
                }
                text = android;
                var match = Prefix.Match(text);
                if (!match.Success)
                {
                    _inner.Emit(logEvent);
                    return;
                }
                var className = match.Groups["class"].Value;
                props.Add(new LogEventProperty("ClassName", new ScalarValue(className)));
                newText = Prefix.Replace(text, "");

            }

            // Copy properties + add ClassName
            foreach (var kv in logEvent.Properties)
            {
                if (kv.Key!= "AndroidCompatMessage")
                    props.Add(new LogEventProperty(kv.Key, kv.Value));
            }

            // Create a new LogEvent with the rewritten template
            var rewritten = new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                logEvent.Exception,
                _parser.Parse(newText),
                props
            );

            _inner.Emit(rewritten);
        }
        public void Dispose() => _innerDispose?.Dispose();
    }
    */
    public sealed class RewriteMessageSink : ILogEventSink, IDisposable
    {
        private static readonly Regex Prefix =
            new(@"^\[(?<class>[^\]]+)\]\s*", RegexOptions.Compiled);

        private readonly ILogEventSink _inner;
        private readonly IDisposable? _innerDispose;
        private readonly MessageTemplateParser _parser = new();

        public RewriteMessageSink(ILogEventSink inner)
        {
            _inner = inner;
            _innerDispose = inner as IDisposable;
        }

        public void Emit(LogEvent logEvent)
        {
            if (!logEvent.Properties.TryGetValue("App", out var appScalar) ||
                appScalar is not ScalarValue { Value: string app })
            {
                _inner.Emit(logEvent);
                return;
            }

            var dict = new Dictionary<string, LogEventPropertyValue>(logEvent.Properties);

            dict.Remove("AndroidCompatMessage");

            string templateText = logEvent.MessageTemplate.Text;

            string? className = null;

            if (app == EnvironmentSetup.AppAndroid)
            {
                if (logEvent.Properties.TryGetValue("AndroidCompatMessage", out var andr) &&
                    andr is ScalarValue { Value: string android })
                {
                    var match = Prefix.Match(android);
                    if (match.Success)
                    {
                        className = match.Groups["class"].Value;
                        templateText = Prefix.Replace(android, "");
                    }
                }
            }
            else
            {
                // Non-Android path: extract from SourceContext
                if (logEvent.Properties.TryGetValue("SourceContext", out var sc) &&
                    sc is ScalarValue { Value: string fullName })
                {
                    className = fullName.Split('.').Last();
                }
            }

            dict["ClassName"] = new ScalarValue(className);
            if (LoggerInfrastructure.PascalClassNameWidth!=-1)
            {
                dict["PascalClassName"] = new ScalarValue(Truncate(className ?? "", LoggerInfrastructure.PascalClassNameWidth));
            }
            var rewritten = new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                logEvent.Exception,
                _parser.Parse(templateText),
                dict.Select(kv => new LogEventProperty(kv.Key, kv.Value)));

            _inner.Emit(rewritten);
        }
        // Split "RepositoryManagerService" -> ["Repository","Manager","Service"]
        private static readonly Regex PascalWords =
            new(@"[A-Z][a-z0-9]*", RegexOptions.Compiled);

        public static string Truncate(string input, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "".PadRight(maxLength);

            if (input.Length <= maxLength)
                return input.PadRight(maxLength);

            var words = PascalWords.Matches(input)
                                   .Select(m => m.Value)
                                   .ToList();

            // If we couldn't split, fallback to substring
            if (words.Count == 0)
                return input[..maxLength];

            // Step 1: start with full words
            var current = string.Concat(words);
            if (current.Length <= maxLength)
                return current.PadRight(maxLength);

            // Step 2: shrink words progressively
            var wordLengths = words.Select(w => w.Length).ToArray();

            while (true)
            {
                int total = wordLengths.Sum();
                if (total <= maxLength)
                    break;

                // shrink the longest word first
                int idx = Array.IndexOf(wordLengths, wordLengths.Max());

                // don't shrink words below 1 char
                if (wordLengths[idx] > 1)
                    wordLengths[idx]--;
                else
                    break;
            }

            // Step 3: rebuild shortened string
            var sb = new StringBuilder(maxLength);
            for (int i = 0; i < words.Count; i++)
                sb.Append(words[i][..wordLengths[i]]);

            var result = sb.ToString();

            // Final safety trim / pad
            if (result.Length > maxLength)
                result = result[..maxLength];

            return result.PadRight(maxLength);
        }
        public void Dispose() => _innerDispose?.Dispose();
    }

    public sealed class ClassNameEnricher : ILogEventEnricher
    {
        private static readonly Regex _regex =
        new(@"\[(?<class>[^\]]+)\]", RegexOptions.Compiled);


        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory pf)
        {

            if (logEvent.Properties.TryGetValue("SourceContext", out var sc))
            {
                if (sc is ScalarValue { Value: string fullName })
                {
                    if (!fullName.StartsWith("Android"))
                    {
                        var className2 = fullName.Split('.').Last();
                        logEvent.AddOrUpdateProperty(pf.CreateProperty("ClassName", className2));
                        return;
                    }
                }
            }
            var text = logEvent.MessageTemplate.Text;
            var match = _regex.Match(text);
            if (!match.Success)
                return;
            var className = match.Groups["class"].Value;
            text = text.Replace($"[{className}]", "").Trim();
            var parser = new MessageTemplateParser();
            var newTemplate = parser.Parse(text);
        }
    }
    public sealed class LoggerAsSink : ILogEventSink, IDisposable
    {
        private readonly Serilog.ILogger _logger;

        public LoggerAsSink(Serilog.ILogger logger) => _logger = logger;

        public void Emit(LogEvent logEvent) => _logger.Write(logEvent);

        public void Dispose()
        {
            if (_logger is IDisposable d) d.Dispose();
        }
    }
    public sealed class LibraryTaggingLoggerFactory : ILoggerFactory
    {
        private readonly ILoggerFactory _innerFactory;
        private readonly ILoggerFactory _androidFactory;
        private readonly ILoggerFactory _mihonFactory;

        public LibraryTaggingLoggerFactory(ILoggerFactory factory)
        {
            _mihonFactory = LoggerFactory.Create(builder =>
            {
                var logger = Log.Logger.ForContext("App", EnvironmentSetup.AppMihon);
                builder.AddSerilog(logger);
            });
            _androidFactory = LoggerFactory.Create(builder =>
            {
                var logger = Log.Logger.ForContext("App", EnvironmentSetup.AppAndroid);
                builder.AddSerilog(logger);
            });
            _innerFactory = factory;
        }

        public void AddProvider(ILoggerProvider provider)
        {
            _innerFactory.AddProvider(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (categoryName.StartsWith("Mihon.ExtensionsBridge"))
            {
                return _mihonFactory.CreateLogger(categoryName);
            }
            else if (categoryName == "Android")
            {
                return _androidFactory.CreateLogger(categoryName);
            }
            return _innerFactory.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            _mihonFactory.Dispose();
            _androidFactory.Dispose();
        }
    }
}

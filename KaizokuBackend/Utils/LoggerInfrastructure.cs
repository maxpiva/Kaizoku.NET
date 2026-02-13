using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Parsing;
using Serilog.Sinks.SystemConsole.Themes;
using System.Text;
using System.Text.RegularExpressions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KaizokuBackend.Utils
{
    public static class LoggerInfrastructure
    {
        public static int PascalClassNameWidth = 0;
        private static readonly (string App, string Colored)[] ConsoleAppStyles =
        [
            (EnvironmentSetup.AppKaizokuNET, "\u001b[32mKaizoku\u001b[0m"),
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
                .Enrich.WithProperty("App", EnvironmentSetup.AppKaizokuNET)
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

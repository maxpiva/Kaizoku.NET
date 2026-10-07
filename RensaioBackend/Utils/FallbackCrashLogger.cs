using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace RensaioBackend.Utils
{
    /// <summary>
    /// Last-resort managed crash diagnostics, independent of Serilog. Each stream is
    /// limited to three 1 MiB files; handled exceptions cannot evict fatal entries.
    /// </summary>
    public static class FallbackCrashLogger
    {
        public const int MaxFileBytes = 1024 * 1024;
        public const int RetainedFileCount = 3;
        public const int MaxEntryBytes = 64 * 1024;
        public const int FirstChanceEntriesPerMinute = 64;
        private static readonly object _lock = new();
        private static readonly HashSet<string> _seen = new();
        private static string? _logPath;
        private static string? _diagnosticPath;
        private static Mutex? _fileMutex;
        private static long _windowStart = Stopwatch.GetTimestamp();
        private static long _suppressed;
        [ThreadStatic] private static bool _writing;

        /// <summary>Call before startup. Only the first successful call takes effect.</summary>
        public static void Initialize(string logsDirectory)
        {
            if (_writing) return;
            _writing = true;
            try
            {
                lock (_lock)
                {
                    if (_logPath != null) return;
                    Directory.CreateDirectory(logsDirectory);
                    var path = Path.GetFullPath(Path.Combine(logsDirectory, "crash-.log"));
                    // Backend and desktop instances using the same directory must serialize
                    // both rotation and append, not merely individual writes.
                    var key = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
                    _fileMutex = new Mutex(false, "RensaioCrashLog-" +
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
                    _diagnosticPath = Path.Combine(Path.GetDirectoryName(path)!, "firstchance-.log");
                    WithFileMutex(() =>
                    {
                        NormalizeExistingFiles(path);
                        NormalizeExistingFiles(_diagnosticPath);
                        Append(path, $"[{DateTime.UtcNow:O}] FallbackCrashLogger initialized.{Environment.NewLine}");
                    });
                    _logPath = path;
                    AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteException(
                        e.ExceptionObject as Exception,
                        "APPDOMAIN UNHANDLED EXCEPTION (terminating=" + e.IsTerminating + ")");
                    if (Environment.GetEnvironmentVariable("RENSAIO_FIRST_CHANCE_LOG") == "1")
                        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
                }
            }
            catch { /* Logging must never prevent startup or recursively log its own failures. */ }
            finally { _writing = false; }
        }

        private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (_writing || _logPath == null) return;
            _writing = true;
            try
            {
                var ex = e.Exception;
                var type = ex.GetType().FullName ?? "";
                // Known throw/catch control flow, not evidence of a crash. This filter
                // affects first-chance diagnostics only; unhandled events always log.
                if (type == "com.russhwolf.settings.serialization.DeserializationException" ||
                    type.StartsWith("com.googlecode.dex2jar.", StringComparison.Ordinal) ||
                    type == "java.lang.ClassNotFoundException" ||
                    type == "java.lang.NoSuchFieldException" ||
                    type == "java.lang.NoSuchMethodException" ||
                    type == "java.time.format.DateTimeParseException") return;
                var source = ex.Source ?? "";
                if (!(source.Contains("IKVM", StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("Mihon", StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("Rensaio", StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("Java", StringComparison.OrdinalIgnoreCase))) return;
                lock (_lock)
                {
                    if (Stopwatch.GetElapsedTime(_windowStart) >= TimeSpan.FromMinutes(1))
                    {
                        FlushSuppressed();
                        _seen.Clear();
                        _windowStart = Stopwatch.GetTimestamp();
                    }
                    // A bounded set limits both memory and I/O, even with unique messages.
                    var signature = Clip(type) + "|" + Clip(source) + "|" + Clip(ex.Message, 512);
                    if (_seen.Count >= FirstChanceEntriesPerMinute || !_seen.Add(signature))
                    {
                        if (_suppressed < long.MaxValue) _suppressed++;
                        return;
                    }
                    WithFileMutex(() => Append(_diagnosticPath!, FormatException(ex,
                        "FIRSTCHANCE: " + ex.GetType().Name + " from " + source, "FirstChance")));
                }
            }
            catch { }
            finally { _writing = false; }
        }

        public static void Write(string message, [CallerMemberName] string caller = "")
        {
            if (_writing || _logPath == null) return;
            _writing = true;
            try { WriteEntryCore($"[{DateTime.UtcNow:O}] [{Clip(caller)}] {Clip(message)}{Environment.NewLine}"); }
            catch { }
            finally { _writing = false; }
        }

        public static void WriteException(Exception? ex, string context, [CallerMemberName] string caller = "")
        {
            if (_writing || _logPath == null) return;
            // Formatting itself can throw (including custom exception property getters).
            _writing = true;
            try { WriteEntryCore(FormatException(ex, context, caller)); }
            catch { }
            finally { _writing = false; }
        }

        private static string Clip(string? value, int limit = 16 * 1024) =>
            value == null ? "" : value.Length <= limit ? value : value[..limit] + " [truncated]";

        private static string FormatException(Exception? ex, string context, string caller)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.UtcNow:O}] [{Clip(caller)}] CRASH: {Clip(context)}");
            if (ex != null)
            {
                sb.AppendLine($"  Type:     {Clip(ex.GetType().FullName)}");
                sb.AppendLine($"  Message:  {Clip(ex.Message)}");
                sb.AppendLine($"  HResult:  0x{ex.HResult:X8}");
                sb.AppendLine($"  StackTrace: {Clip(ex.StackTrace)}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"  InnerException: {Clip(ex.InnerException.GetType().FullName)}: {Clip(ex.InnerException.Message)}");
                    sb.AppendLine($"  InnerStackTrace: {Clip(ex.InnerException.StackTrace)}");
                }
                sb.AppendLine($"  Source:   {Clip(ex.Source)}");
                sb.AppendLine($"  TargetSite: {ex.TargetSite}");
            }
            sb.AppendLine("--- end crash entry ---");
            return sb.ToString();
        }

        private static void WriteEntryCore(string entry)
        {
            lock (_lock)
            {
                // Fatal output is independent of diagnostic quotas and summaries.
                WithFileMutex(() => Append(_logPath!, entry));
                FlushSuppressed();
            }
        }

        private static void FlushSuppressed()
        {
            if (_suppressed == 0) return;
            WithFileMutex(() => Append(_diagnosticPath!,
                $"[{DateTime.UtcNow:O}] FIRSTCHANCE: suppressed {_suppressed} repeated/rate-limited exceptions.{Environment.NewLine}"));
            _suppressed = 0;
        }

        private static void WithFileMutex(Action action)
        {
            var acquired = false;
            try
            {
                try { acquired = _fileMutex!.WaitOne(TimeSpan.FromSeconds(2)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException("Crash log is busy.");
                action();
            }
            finally { if (acquired) _fileMutex!.ReleaseMutex(); }
        }

        private static string Archive(string path, int index) => index == 0 ? path : path + "." + index;

        private static void NormalizeExistingFiles(string path)
        {
            for (var i = 0; i < RetainedFileCount; i++)
            {
                var file = Archive(path, i);
                if (!File.Exists(file) || new FileInfo(file).Length <= MaxFileBytes) continue;
                // Upgrade an oversized legacy file in bounded memory, preserving its tail.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                var tail = new byte[MaxFileBytes];
                stream.Seek(-MaxFileBytes, SeekOrigin.End);
                stream.ReadExactly(tail);
                var start = 0;
                while (start < tail.Length && (tail[start] & 0xC0) == 0x80) start++;
                stream.Position = 0;
                stream.Write(tail.AsSpan(start));
                stream.SetLength(tail.Length - start);
            }
        }

        private static void Append(string path, string entry)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            if (bytes.Length > MaxEntryBytes)
            {
                var marker = Encoding.UTF8.GetBytes("\n[entry truncated]\n--- end crash entry ---\n");
                var keep = MaxEntryBytes - marker.Length;
                // Do not split a UTF-8 sequence at the truncation boundary.
                while ((bytes[keep] & 0xC0) == 0x80) keep--;
                bytes = bytes[..keep].Concat(marker).ToArray();
            }
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaxFileBytes)
            {
                for (var i = RetainedFileCount - 1; i > 0; i--)
                {
                    var previous = Archive(path, i - 1);
                    if (File.Exists(previous)) File.Move(previous, Archive(path, i), true);
                }
            }
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(true);
        }
    }
}

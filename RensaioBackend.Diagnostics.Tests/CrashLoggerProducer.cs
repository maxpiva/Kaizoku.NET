using RensaioBackend.Utils;

namespace RensaioBackend.Diagnostics.Tests
{
    // Each scenario runs in a fresh process: static initialization and the real
    // AppDomain hooks must be exercised, not reset or invoked through reflection.
    public static class CrashLoggerProducer
    {
        public static void Main(string[] args)
        {
            var mode = args[0];
            var path = args[1];
            Environment.SetEnvironmentVariable("RENSAIO_FIRST_CHANCE_LOG",
                mode == "default" || mode == "fatal" ? null : "1");
            Parallel.For(0, 8, _ => FallbackCrashLogger.Initialize(path));
            if (mode == "fatal" || mode == "known-fatal" || mode == "diagnostic-rotation")
            {
                // These exceptions really terminate the process, after diagnostic
                // quotas are exhausted; the unhandled producer must still persist them.
                if (mode == "diagnostic-rotation")
                {
                    for (var i = 0; i < FallbackCrashLogger.FirstChanceEntriesPerMinute; i++)
                    {
                        try
                        {
                            throw new Exception(i + new string('界', 100000),
                                new Exception(new string('界', 100000))) { Source = "Android.Compat" };
                        }
                        catch { }
                    }
                }
                else Flood();
                if (mode == "known-fatal")
                    throw new com.russhwolf.settings.serialization.DeserializationException("fatal-settings-marker");
                throw new InvalidOperationException("fatal-marker", new Exception("inner-fatal-marker"));
            }
            if (mode == "default" || mode == "diagnostics")
            {
                Flood();
                for (var i = 0; i < 1000; i++)
                {
                    try { throw new com.russhwolf.settings.serialization.DeserializationException("known-settings-noise"); }
                    catch { }
                    try { throw new java.time.format.DateTimeParseException("known-date-noise"); }
                    catch { }
                }
                FallbackCrashLogger.Write("finished-marker");
            }
            if (mode == "rotation")
            {
                for (var i = 0; i < 500; i++)
                    FallbackCrashLogger.WriteException(new Exception(new string('界', 100000),
                        new Exception(new string('界', 100000))), "rotation-" + i);
                FallbackCrashLogger.WriteException(new Exception("last-fatal-marker"), "fatal-after-rotation");
            }
            if (mode == "threads")
            {
                Parallel.For(0, 512, i => FallbackCrashLogger.Write("BEGIN-" + i + "-" + new string('x', 8192) + "-END-" + i));
                FallbackCrashLogger.Write("finished-marker");
            }
            if (mode == "processes")
            {
                for (var i = 0; i < 128; i++)
                    FallbackCrashLogger.Write("BEGIN-" + i + "-" + new string('x', 8192) + "-END-" + i);
            }
            if (mode == "legacy" || mode == "unwritable")
                FallbackCrashLogger.WriteException(new Exception("after-initialize-marker"), "initialization");
        }

        private static void Flood()
        {
            for (var i = 0; i < 20000; i++)
            {
                try { throw new Exception("repeated-marker") { Source = "Android.Compat" }; }
                catch { }
            }
            for (var i = 0; i < 100; i++)
            {
                try { throw new Exception("distinct-marker-" + i) { Source = "IKVM.Java" }; }
                catch { }
            }
        }
    }
}

// Stand-ins supply exact bridge exception names without loading Java/native code.
// They are thrown/caught through the real first-chance event producer.
namespace com.russhwolf.settings.serialization
{
    public sealed class DeserializationException : Exception
    {
        public DeserializationException(string message) : base(message) { Source = "Android.Compat"; }
    }
}
namespace java.time.format
{
    public sealed class DateTimeParseException : Exception
    {
        public DateTimeParseException(string message) : base(message) { Source = "IKVM.Java"; }
    }
}

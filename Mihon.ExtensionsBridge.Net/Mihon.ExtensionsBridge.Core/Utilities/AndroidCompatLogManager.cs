using Microsoft.Extensions.Logging;
using System;
using System.Text;
using extension.bridge.logging;

namespace Mihon.ExtensionsBridge.Core.Utilities
{
    internal static class AndroidCompatLogManager
    {
        private static readonly object SyncRoot = new();


        public static void SetLoglevel(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            lock (SyncRoot)
            {
           
                var minimum = DetermineMinimumLevel(logger);
                AndroidCompatLogBridge.setMinimumLevel(minimum);
            }
        }

        private static AndroidCompatLogLevel DetermineMinimumLevel(ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                return AndroidCompatLogLevel.VERBOSE;
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                return AndroidCompatLogLevel.DEBUG;
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                return AndroidCompatLogLevel.INFO;
            }

            if (logger.IsEnabled(LogLevel.Warning))
            {
                return AndroidCompatLogLevel.WARN;
            }

            if (logger.IsEnabled(LogLevel.Error))
            {
                return AndroidCompatLogLevel.ERROR;
            }

            return AndroidCompatLogLevel.ASSERT;
        }

        private static LogLevel MapLevel(AndroidCompatLogLevel level)
        {
            if (Equals(level, AndroidCompatLogLevel.VERBOSE))
            {
                return LogLevel.Trace;
            }
            if (Equals(level, AndroidCompatLogLevel.DEBUG))
            {
                return LogLevel.Debug;
            }
            if (Equals(level, AndroidCompatLogLevel.INFO))
            {
                return LogLevel.Information;
            }
            if (Equals(level, AndroidCompatLogLevel.WARN))
            {
                return LogLevel.Warning;
            }
            if (Equals(level, AndroidCompatLogLevel.ERROR))
            {
                return LogLevel.Error;
            }
            if (Equals(level, AndroidCompatLogLevel.ASSERT))
            {
                return LogLevel.Critical;
            }
            return LogLevel.Information;
        }

        public sealed class LoggerSink : AndroidCompatLogSink
        {
            private ILogger _logger;

            public LoggerSink(ILogger logger)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public void UpdateLogger(ILogger logger)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public void log(AndroidCompatLogLevel level, string tag, string message, string throwable)
            {
                var logLevel = MapLevel(level);
                var logger = _logger;

                if (!logger.IsEnabled(logLevel))
                {
                    return;
                }

                var builder = new StringBuilder();
                if (!string.IsNullOrEmpty(tag))
                {
                    builder.Append('[').Append(tag).Append("] ");
                }

                builder.Append(message);

                if (!string.IsNullOrEmpty(throwable))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(throwable);
                }

                var finalMessage = builder.ToString();
                logger.Log(logLevel, "{AndroidCompatMessage}", finalMessage);
            }
        }
    }
}

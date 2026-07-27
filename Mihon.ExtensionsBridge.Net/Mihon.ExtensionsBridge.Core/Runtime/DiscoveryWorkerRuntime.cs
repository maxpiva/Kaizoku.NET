using extension.bridge;
using ikvm.runtime;
using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System.Reflection;

namespace Mihon.ExtensionsBridge.Core.Runtime
{
    /// <summary>
    /// Minimal, DI-free bridge bootstrap for short-lived discovery worker processes.
    /// Mirrors <see cref="BridgeHost"/>'s android-compat initialization but skips everything a
    /// worker must never do: no CEF, no repository refresh, no local-extension initialization and
    /// no writes to the main process's bridge folder (the worker gets its own scratch folders).
    /// </summary>
    public static class DiscoveryWorkerRuntime
    {
        private sealed class StaticWorkingFolderStructure : IWorkingFolderStructure
        {
            public string WorkingFolder { get; init; } = string.Empty;
            public string ExtensionsFolder { get; init; } = string.Empty;
            public string AndroidFolder { get; init; } = string.Empty;
            public string TempFolder { get; init; } = string.Empty;
        }

        /// <summary>
        /// Boots the IKVM/android compatibility layer for this process. Must be called exactly once,
        /// before any extension JAR is loaded. Same ordering constraint as BridgeHost: nothing may
        /// touch Kotlin-side settings before applicationSetup registers the SettingsConfig module.
        /// </summary>
        public static void Initialize(string androidFolder, string tempFolder, ILogger androidLogger)
        {
            Directory.CreateDirectory(androidFolder);
            Directory.CreateDirectory(tempFolder);

            var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            Startup.addBootClassPathAssembly(Assembly.LoadFrom(Path.Combine(baseDir, "Android.Compat.dll")));
            StartupKt.applicationSetup(androidFolder, tempFolder, new AndroidCompatLogManager.LoggerSink(androidLogger), false);
            AndroidCompatLogManager.SetLoglevel(androidLogger);

            // The worker's stdout is a machine-parsed protocol channel owned by the parent process.
            // Extension/OkHttp/android-compat code loves to print via Java System.out, which IKVM
            // routes to fd 1 — redirect both Java standard streams to stderr (after applicationSetup,
            // so nothing can undo it, and before any extension JAR is loaded) where they become
            // harmless worker log lines instead of stray/corrupting stdout writes.
            RedirectJavaConsoleToStderr();
        }

        /// <summary>
        /// Points Java's System.out and System.err at the process's real stderr (fd 2).
        /// </summary>
        private static void RedirectJavaConsoleToStderr()
        {
            var stderr = new java.io.PrintStream(new java.io.FileOutputStream(java.io.FileDescriptor.err), true);
            java.lang.System.setOut(stderr);
            java.lang.System.setErr(stderr);
        }

        /// <summary>
        /// Pushes the parent process's preferences (FlareSolverr, SOCKS proxy, interceptor overrides)
        /// into this worker's Kotlin-side settings. Unlike <see cref="BridgeManager.SetPreferencesAsync"/>
        /// this sets unconditionally — the worker always starts from a default config — and never
        /// persists anything to disk. Failures are logged and ignored; searches then run with defaults.
        /// </summary>
        public static void ApplyPreferences(Mihon.ExtensionsBridge.Models.Preferences prefs, ILogger logger)
        {
            try
            {
                SettingsConfig.Settings config;
                try
                {
                    config = ConfigKt.getSettings();
                }
                catch
                {
                    config = new SettingsConfig.Settings();
                }
                var interceptors = new java.util.HashMap();
                foreach (var outer in prefs.Interceptors)
                {
                    var inner = new java.util.HashMap();
                    foreach (var kv in outer.Value)
                        inner.put(kv.Key, kv.Value);
                    interceptors.put(outer.Key, inner);
                }
                config.setInterceptorOverrides(interceptors);
                if (prefs.FlareSolverr != null)
                {
                    config.setFlareSolverrEnabled(prefs.FlareSolverr.Enabled);
                    config.setFlareSolverrUrl(prefs.FlareSolverr.Url ?? "http://127.0.0.1:8189");
                    config.setFlareSolverrTimeout(prefs.FlareSolverr.Timeout);
                    config.setFlareSolverrSessionName(prefs.FlareSolverr.SessionName);
                    config.setFlareSolverrSessionTtl(prefs.FlareSolverr.SessionTtl);
                    config.setFlareSolverrAsResponseFallback(prefs.FlareSolverr.AsResponseFallback);
                }
                if (prefs.SocksProxy != null)
                {
                    config.setSocksProxyEnabled(prefs.SocksProxy.Enabled);
                    config.setSocksProxyHost(prefs.SocksProxy.Host ?? "");
                    config.setSocksProxyPort(prefs.SocksProxy.Port == 0 ? "" : prefs.SocksProxy.Port.ToString());
                    config.setSocksProxyUsername(prefs.SocksProxy.Username);
                    config.setSocksProxyPassword(prefs.SocksProxy.Password);
                }
                ConfigKt.setSettings(config);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply preferences in discovery worker; continuing with defaults.");
            }
        }

        /// <summary>
        /// Classloads an extension JAR from an explicit folder (the parent's discovery cache) and
        /// returns its interop. Blocking and potentially throwing — callers own the error handling.
        /// </summary>
        public static IExtensionInterop LoadExtension(RepositoryEntry entry, string jarFolder, string scratchExtensionsFolder, string tempFolder, ILogger logger)
        {
            var structure = new StaticWorkingFolderStructure
            {
                WorkingFolder = scratchExtensionsFolder,
                ExtensionsFolder = scratchExtensionsFolder,
                AndroidFolder = string.Empty,
                TempFolder = tempFolder
            };
            return new JarExtensionInterop(structure, entry, logger, jarFolder);
        }

        /// <summary>
        /// Best-effort android shutdown, mirroring <see cref="BridgeHost"/>. The worker process exits
        /// immediately afterwards regardless, so failures here are only logged.
        /// </summary>
        public static void Shutdown(ILogger logger)
        {
            try
            {
                ((Action)(() =>
                {
                    StartupKt.applicationShutdown(extension.bridge.logging.AndroidCompatLoggerKt.androidCompatLogger(typeof(DiscoveryWorkerRuntime)));
                })).InvokeInJavaContext();
            }
            catch (java.lang.IllegalStateException ex) when (ex.Message?.Contains("Main thread not allowed to quit") == true)
            {
                logger.LogInformation("Main thread already quitting, worker android shutdown proceeding.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Worker android shutdown failed; process will exit anyway.");
            }
        }
    }
}

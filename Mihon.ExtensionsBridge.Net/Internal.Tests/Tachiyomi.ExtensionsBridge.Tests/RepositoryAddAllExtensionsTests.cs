using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.IKVMCompiler;
using Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;
using Mihon.ExtensionsBridge.IKVMCompiler.Services;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Tachiyomi.ExtensionsBridge.Tests
{
    // Simple file logger
    internal sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;
        private readonly object _lock = new();
        public FileLoggerProvider(string path) { _path = path; }    
        public ILogger CreateLogger(string categoryName) => new FileLogger(_path, _lock, categoryName);
        public void Dispose() { }
        private sealed class FileLogger : ILogger
        {
            private readonly string _path;
            private readonly object _lock;
            private readonly string _category;
            public FileLogger(string path, object sync, string category) { _path = path; _lock = sync; _category = category; }
            public IDisposable BeginScope<TState>(TState state) => default!;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var line = $"{DateTimeOffset.Now:O} [{logLevel}] {_category}: {formatter(state, exception)}";
                if (exception != null) line += $"\n{exception}";
                lock (_lock) { System.IO.File.AppendAllText(_path, line + Environment.NewLine); }
            }
        }
    }

    public class RepositoryAddAllExtensionsTests
    {
        // Plan:
        // - Build and start a generic host:
        //   - Create HostBuilder.
        //   - Configure logging using provided ILoggerFactory.
        //   - Register Paths options and AddExtensionsBridge services.
        //   - Start the host to run background services required by AddExtensionsBridge.
        // - Use host.Services as the ServiceProvider in the test.
        // - Await bridge initialization before proceeding.
        // - Add online repository, iterate extensions, add each one, and log.
        // - Stop host at the end.

        private static async Task<IHost> BuildAndStartHostAsync(ILoggerFactory loggerFactory)
        {
            var paths = new Mihon.ExtensionsBridge.Models.Configuration.Paths
            {
                BridgeFolder = "C:\\temp\\ExtensionBridgeWork"
            };

            var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(loggerFactory);
                    services.AddSingleton<IOptions<Mihon.ExtensionsBridge.Models.Configuration.Paths>>(Options.Create(paths));
                    services.AddExtensionsBridge();
                    services.AddIKVMCompiler();
                })
                .Build();

            await host.StartAsync();
            return host;
        }


        [Fact]
        public async Task IKVMCompileAll()
        {
            var logFile = System.IO.Path.Combine("C:\\temp\\ExtensionBridgeWork", "AddAllExtensions.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logFile)!);

            using var loggerFactory = LoggerFactory.Create(builder => builder
                .AddSimpleConsole(o =>
                {
                    o.SingleLine = false;
                    o.IncludeScopes = true;
                    o.TimestampFormat = "HH:mm:ss ";
                })
                .AddProvider(new FileLoggerProvider(logFile))
                .SetMinimumLevel(LogLevel.Debug));
            ILogger logger = loggerFactory.CreateLogger("Test");

            using var host = await BuildAndStartHostAsync(loggerFactory);
            var sp = host.Services;
            var cdownloader = sp.GetRequiredService<IIkvmCompilerDownloader>();
            var compiler = sp.GetRequiredService<IIkvmCompiler>();
            await cdownloader.CompilerDownloadAsync();
            string directoryPath = "C:\\temp\\ExtensionBridgeWork\\extensions";
            string pattern = "*.jar";
            foreach (var file in System.IO.Directory.EnumerateFiles(directoryPath, pattern, System.IO.SearchOption.AllDirectories))
            {
                string jar = System.IO.Path.GetFullPath(file);
                await compiler.CompileAsync(jar);
            }
            logger.LogInformation("All jar compiled.");
        }


        [Fact]
        public async Task AddAllExtensionsFromRepo_LogsAndSucceeds()
        {
            var logFile = System.IO.Path.Combine("C:\\temp\\ExtensionBridgeWork", "AddAllExtensions.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logFile)!);

            using var loggerFactory = LoggerFactory.Create(builder => builder
                .AddSimpleConsole(o =>
                {
                    o.SingleLine = false;
                    o.IncludeScopes = true;
                    o.TimestampFormat = "HH:mm:ss ";
                })
                .AddProvider(new FileLoggerProvider(logFile))
                .SetMinimumLevel(LogLevel.Debug));
            ILogger logger = loggerFactory.CreateLogger("Test");

            using var host = await BuildAndStartHostAsync(loggerFactory);
            var sp = host.Services;

            var bridge = sp.GetRequiredService<IBridgeManager>();
            while (!bridge.Initialized)
            {
                await Task.Delay(100);
            }

            var repoMgr = bridge.OnlineRepositoryManager;
            var repo = new TachiyomiRepository("https://raw.githubusercontent.com/keiyoushi/extensions/repo");

            var added = await repoMgr.AddOnlineRepositoryAsync(repo);
            var repos = await repoMgr.ListOnlineRepositoryAsync();
            repo = repos.Find(r => r.Url == repo.Url) ?? repo;

            var extMgr = bridge.LocalExtensionManager;
            foreach (var v in repo.Extensions)
            {
                logger.LogInformation("Adding extension: {0} v{1}", v.Name, v.Version);
                try
                {
                    await extMgr.AddExtensionAsync(v);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                    throw;
                }
            }

            logger.LogInformation("Repository add & refresh completed.");

            await host.StopAsync();
        }

        // PSEUDOCODE:
        // - Validate inputs: directoryPath and extension must be non-empty.
        // - If the directory does not exist, return empty list.
        // - Normalize extension to ensure it starts with '.'.
        // - Build a search pattern "*.<extension>".
        // - Use Directory.EnumerateFiles with AllDirectories to recurse.
        // - Convert each found file to full path and collect into a list.
        // - Return the list.

        private static System.Collections.Generic.List<string> GetFilesByExtension(string directoryPath, string extension)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new System.ArgumentException("directoryPath cannot be null or empty.", nameof(directoryPath));

            if (string.IsNullOrWhiteSpace(extension))
                throw new System.ArgumentException("extension cannot be null or empty.", nameof(extension));

            if (!System.IO.Directory.Exists(directoryPath))
                return new System.Collections.Generic.List<string>();

            var normalizedExt = extension.StartsWith('.') ? extension : "." + extension;
            var pattern = "*" + normalizedExt;

            var results = new System.Collections.Generic.List<string>();
            foreach (var file in System.IO.Directory.EnumerateFiles(directoryPath, pattern, System.IO.SearchOption.AllDirectories))
            {
                results.Add(System.IO.Path.GetFullPath(file));
            }

            return results;
        }
    }
}

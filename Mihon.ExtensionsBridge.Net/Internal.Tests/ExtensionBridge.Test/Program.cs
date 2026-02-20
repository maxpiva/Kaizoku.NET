using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Runtime;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Services;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    Mihon.ExtensionsBridge.Models.Configuration.Paths paths = new Mihon.ExtensionsBridge.Models.Configuration.Paths
                    {
                        BridgeFolder = "C:\\temp\\ExtensionBridgeWork"
                    };
                    services.AddSingleton<IOptions<Mihon.ExtensionsBridge.Models.Configuration.Paths>>(Options.Create(paths));
                    services.AddExtensionsBridge();
                    services.AddHostedService<AppHostedService>();
                })
                .Build();

            await host.RunAsync();
        }
    }

    public class AppHostedService : IHostedService
    {
        private readonly ILogger<AppHostedService> _logger;
        private readonly IBridgeManager _bridge;



        public AppHostedService(ILogger<AppHostedService> logger,
            IBridgeManager bridge)
        {
            _logger = logger;
            _bridge = bridge;
        }
       
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            string nn = Assembly.GetExecutingAssembly().GetName().FullName;

            _logger.LogInformation("Application started");
            while(!_bridge.Initialized)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            /*
            var repoMgr = _bridge.OnlineRepositoryManager;
            var repo = new TachiyomiRepository("https://raw.githubusercontent.com/keiyoushi/extensions/repo");

            var added = await repoMgr.AddOnlineRepositoryAsync(repo);
            var repos = await repoMgr.ListOnlineRepositoryAsync();
            repo = repos.Find(r => r.Url == repo.Url) ?? repo;

            var extMgr = _bridge.LocalExtensionManager;
            foreach (var v in repo.Extensions)
            {
                try
                {
                    await extMgr.AddExtensionAsync(v);
                }
                catch (Exception e)
                {
                    throw;
                }
            }
            */
       

            IRepositoryManager repoMgr = _bridge.OnlineRepositoryManager;
            IExtensionManager _extManager = _bridge.LocalExtensionManager;
            TachiyomiRepository repo = new TachiyomiRepository("https://raw.githubusercontent.com/keiyoushi/extensions/repo");

            await repoMgr.AddOnlineRepositoryAsync(repo, cancellationToken);

            var list = await repoMgr.ListOnlineRepositoryAsync();


            
            var n = list[0].Extensions.FirstOrDefault(a => a.Name.Contains("Bat"));
            RepositoryGroup grp = await _extManager.AddExtensionAsync(n);
            if (grp!=null)
            {
                IExtensionInterop extension = await _extManager.GetInteropAsync(grp);
                List<ISourceInterop> sources = extension.Sources;
                var prefs = await extension.LoadPreferencesAsync(cancellationToken); ;
                ISourceInterop source = sources.FirstOrDefault()!;
                MangaList mangas3 = await source.GetPopularAsync(1, cancellationToken);
                MangaList mangas = await source.GetLatestAsync(1, cancellationToken);
                MangaList mangas2 = await source.SearchAsync(1, "Infinite", cancellationToken);
                Manga m = await source.GetDetailsAsync(mangas.Mangas[0], cancellationToken);
                List<Chapter> chapters = await source.GetChaptersAsync(m, cancellationToken);
                Chapter chapter = chapters.Last();
                List<Page> pages = await source.GetPagesAsync(chapter, cancellationToken);
            }
           
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Application stopping...");
            return Task.CompletedTask;
        }
    }
}

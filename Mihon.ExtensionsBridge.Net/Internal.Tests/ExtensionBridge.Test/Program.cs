using app.cash.quickjs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Services;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Reflection;
using System.Runtime;
using System.Text.Encodings.Web;
using System.Text.Json;


namespace Mihon.ExtensionsBridge.Test
{
   

    internal static class Program
    {
        static async Task Main(string[] args)
        {
            //IMPORTANT, First pass will create the settings, for a correct verification of any extension,
            //the settings must have FlareSolverr configured, so, go there, edit settings according.
            //After running this one time.
            string workingDirectory = Path.Combine(Environment.CurrentDirectory, "ExtensionBridgeWork");
            if (!Directory.Exists(workingDirectory))
                Directory.CreateDirectory(workingDirectory);
            using IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    // Trace surfaces DexNewInstanceCorrector per-method diagnostics
                    // (live-iterator rewrite / oracle-mismatch / skipped-method).
                    logging.SetMinimumLevel(LogLevel.Trace);
                })
                .ConfigureServices((context, services) =>
                {
                    Mihon.ExtensionsBridge.Models.Configuration.Paths paths = new Mihon.ExtensionsBridge.Models.Configuration.Paths
                    {
                        BridgeFolder = workingDirectory
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
        /*
        public class ScriptModel
        {
            public string imageDecryptEval { get; set; }
            public object postDecryptEval { get; set; }
            public bool shouldVerifyLinks { get; set; }
        }*/


        public async Task TestSourceAsync(IExtensionManager _extManager, RepositoryGroup grp, CancellationToken token)
        {
            _logger.LogInformation("Checking {name}...", grp.Name);
            IExtensionInterop extension = await _extManager.GetInteropAsync(grp);
            List<ISourceInterop> sources = extension.Sources;
            ISourceInterop source = sources.FirstOrDefault()!;
            MangaList? mangas = null;
            MangaList mangas3 = await source.GetPopularAsync(1,token);
            if (source.SupportsLatest)
                mangas = await source.GetLatestAsync(1, token);
            MangaList mangas2 = await source.SearchAsync(1, "Sword", token);
            Manga? m = mangas?.Mangas.FirstOrDefault() ?? mangas3.Mangas.FirstOrDefault() ?? mangas2.Mangas.FirstOrDefault();
            if (m == null)
            {
                _logger.LogWarning("Unable to find any manga to test");
            }
            else
            {
                MangaUpdate mm = await source.GetDetailsAndChaptersAsync(m, token);
                if (mm.Chapters==null || mm.Chapters.Count==0)
                {
                    _logger.LogWarning("Manga: {manga} has no chapters", m.Title);
                }
                else
                {
                    Chapter chap= mm.Chapters.Last();
                    List<Page> pages = await source.GetPagesAsync(chap, token);
                    if (pages==null || pages.Count==0)
                    {
                        _logger.LogWarning("Manga: {manga} chapter {chapter} has no pages", m.Title, chap.Name ?? chap.ChapterNumber.ToString());
                    }
                }

            }
            _logger.LogInformation("Check {name} COMPLETE", grp.Name);
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            string nn = Assembly.GetExecutingAssembly().GetName().FullName;

            _logger.LogInformation("Application started");
            while(!_bridge.Initialized)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            IRepositoryManager repoMgr = _bridge.OnlineRepositoryManager;
            IExtensionManager _extManager = _bridge.LocalExtensionManager;
            TachiyomiRepository repo = new TachiyomiRepository("https://raw.githubusercontent.com/keiyoushi/extensions/repo");

            await repoMgr.AddOnlineRepositoryAsync(repo, cancellationToken);

            //SMOKE TEST
            var list = repoMgr.ListOnlineRepositories();
            foreach (var v in list[0].Extensions)
            {
                try
                {
                    _logger.LogInformation("Installing {name}...", v.Name);
                    RepositoryGroup? repoGroup = await _extManager.AddExtensionAsync(v);
                    if (repoGroup==null)
                    {
                        _logger.LogError("Unable to install Extension {ext}", v.Name);
                    }
                    else
                    {
                        _logger.LogInformation("Installation of {name} COMPLETE", v.Name);
                        await TestSourceAsync(_extManager, repoGroup, cancellationToken);
                    }
                }

                catch (Exception e)
                {
                    _logger.LogError(e, e.ToString());
                }
            }


            //ReadComicOnline javascript decrypter

            // RepositoryGroup grp = await _extManager.AddExtensionAsync(data);
            /*
             var n = list[0].Extensions.FirstOrDefault(a => a.Name.Contains("ReadComicOnline"));
             RepositoryGroup grp = await _extManager.AddExtensionAsync(n);
             if (grp!=null)
             {
                 IExtensionInterop extension = await _extManager.GetInteropAsync(grp);
                 List<ISourceInterop> sources = extension.Sources;
                 var prefs = await extension.LoadPreferencesAsync(cancellationToken);
                 prefs[0].Preference.CurrentValue = "https://plainraw.com/raw/7388602029b1";
                 await extension.SavePreferencesAsync(prefs, cancellationToken);
                 prefs = await extension.LoadPreferencesAsync(cancellationToken);
                 ISourceInterop source = sources.FirstOrDefault()!;
                 MangaList mangas3 = await source.GetPopularAsync(1, cancellationToken);
                 MangaList mangas = await source.GetLatestAsync(1, cancellationToken);
                 MangaList mangas2 = await source.SearchAsync(1, "Absolute", cancellationToken);
                 Manga m = await source.GetDetailsAsync(mangas.Mangas[0], cancellationToken);
                 List<ParsedChapter> chapters = await source.GetChaptersAsync(m, cancellationToken);
                 ParsedChapter chapter = chapters.Last();
                 List<Page> pages = await source.GetPagesAsync(chapter, cancellationToken);
             }
            */

            //SPECIFIC SOURCE
            /*
            var n = list[0].Extensions.FirstOrDefault(a => a.Name.Contains("Hive Scans"));
            RepositoryGroup grp = await _extManager.AddExtensionAsync(n);
            if (grp != null)
            {
                IExtensionInterop extension = await _extManager.GetInteropAsync(grp);
                List<ISourceInterop> sources = extension.Sources;
                ISourceInterop source = sources.FirstOrDefault()!;
                MangaList mangas3 = await source.GetPopularAsync(1, cancellationToken);
                MangaList mangas = await source.GetLatestAsync(1, cancellationToken);
                MangaList mangas2 = await source.SearchAsync(1, "Sword", cancellationToken);
                Manga m = await source.GetDetailsAsync(mangas.Mangas[0], cancellationToken);
                List<ParsedChapter> chapters = await source.GetChaptersAsync(m, cancellationToken);
                MangaUpdate mm = await source.GetDetailsAndChaptersAsync(m, cancellationToken);
                List<Page> pages = await source.GetPagesAsync(chapters.Last(), cancellationToken);
            }
            */
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Application stopping...");
            return Task.CompletedTask;
        }
    }
}

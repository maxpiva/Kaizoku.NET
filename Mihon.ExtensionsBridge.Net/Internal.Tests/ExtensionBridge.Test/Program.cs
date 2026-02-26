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
using static kotlin.reflect.jvm.@internal.ReflectProperties;

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
        public class ScriptModel
        {
            public string imageDecryptEval { get; set; }
            public object postDecryptEval { get; set; }
            public bool shouldVerifyLinks { get; set; }
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

            var list = repoMgr.ListOnlineRepositories();

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36 Edg/145.0.0.0");
            var json = await httpClient.GetStringAsync("https://plainraw.com/raw/7388602029b1");
            var model = JsonSerializer.Deserialize<ScriptModel>(json);
            var html = await httpClient.GetStringAsync("https://readcomiconline.li/Comic/Hyde-Street/Issue-10?id=243622&s=&quality=hq&readType=1");
            string serVal = JavaScriptEncoder.Default.Encode(html.Trim());
            string eval = $"let _encryptedString = \"{serVal}\";let _useServer2 = true;{model.imageDecryptEval}";

            try
            {
                var k = QuickJs.create();
                var m = k.evaluate(eval);
              

                int a = 1;
            }
            catch (Exception e)
            {

            }
            byte[] data = File.ReadAllBytes("C:\\users\\mpiva\\downloads\\tachiyomi-en.readcomiconline-v1.4.39-debug.apk");

            RepositoryGroup grp = await _extManager.AddExtensionAsync(data);
            //var n = list[0].Extensions.FirstOrDefault(a => a.Name.Contains("ReadComicOnline"));
            //RepositoryGroup grp = await _extManager.AddExtensionAsync(n);
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
                MangaList mangas2 = await source.SearchAsync(1, "Absolute Batman", cancellationToken);
                Manga m = await source.GetDetailsAsync(mangas.Mangas[0], cancellationToken);
                List<ParsedChapter> chapters = await source.GetChaptersAsync(m, cancellationToken);
                ParsedChapter chapter = chapters.Last();
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

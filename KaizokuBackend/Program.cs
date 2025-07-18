using KaizokuBackend.Utils;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace KaizokuBackend
{
    public class Program
    {

        public static async Task Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args[0].StartsWith("java") || args[0].StartsWith("xvfb-run"))
                    EnvironmentSetup.JavaRunner = args[0];
            }

            await EnvironmentSetup.InitializeAsync(null);
            await CreateHostBuilder(args).Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {

            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseWebRoot(Path.Combine(EnvironmentSetup.Configuration!["runtimeDirectory"]!, "wwwroot"));
                    webBuilder.ConfigureAppConfiguration(AppConfiguration);
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(server =>
                    {
                        var config = EnvironmentSetup.Configuration!;
                        var port = config.GetValue<int>(
#if DEBUG
                            "Kestrel:Ports:Debug"
#else
    "Kestrel:Ports:Release"
#endif
                            , 5001);
                        EnvironmentSetup.Logger.LogInformation("Starting Kaizoku NET on port {port}...", port);
                        server.ListenAnyIP(port);
                    });
                }).UseSerilog(Log.Logger, dispose: false);
        }

        private static void AppConfiguration(WebHostBuilderContext context, IConfigurationBuilder builder)
        {
            EnvironmentSetup.AddConfigurations(builder);
        }
    }
}
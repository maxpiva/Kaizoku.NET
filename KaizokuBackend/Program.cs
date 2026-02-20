using KaizokuBackend.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace KaizokuBackend
{
    public class Program
    {

        public static async Task Main(string[] args)
        {

            await EnvironmentSetup.InitializeAsync(null);

            var host = CreateHostBuilder(args).Build();

            await host.RunAsync();
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
                            , 5005);
                        EnvironmentSetup.Logger.LogInformation("Starting Kaizoku NET on port {port}...", port);
                        server.ListenAnyIP(port);
                    });
                });
        }

        private static void AppConfiguration(WebHostBuilderContext context, IConfigurationBuilder builder)
        {
            EnvironmentSetup.AddConfigurations(builder);
        }
    }
}
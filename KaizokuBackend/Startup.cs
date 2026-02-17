using KaizokuBackend.Data;
using KaizokuBackend.Hubs;
using KaizokuBackend.Models;
using KaizokuBackend.Services;
using KaizokuBackend.Services.Background;
using KaizokuBackend.Utils;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models.Configuration;
using Serilog;
using Serilog.Extensions.Logging;
using sun.java2d;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KaizokuBackend
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        private ILogger? _logger;

        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                    _logger = LoggerInfrastructure.CreateAppLogger<Startup>(EnvironmentSetup.AppKaizokuNET);
                return _logger;
            }
        }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            Serilog.ILogger logger = Log.Logger;
            services.AddSerilog(logger, false, null);
            services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(sp =>
            {
                return new LibraryTaggingLoggerFactory(new SerilogLoggerFactory(Log.Logger, false));
            }));

            Logger.LogInformation("Initializing Kaizoku .NET...");

            services.AddOpenApi();
            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
            services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(10));

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
#if DEBUG
                    policy.WithOrigins("http://localhost:5001", "http://localhost:3000")
                        .AllowAnyHeader()
                        .AllowAnyMethod().AllowCredentials();
#else
                    policy.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
#endif
                });
            });
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                services.AddScoped<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
            });
            services.AddSignalR();
            services.AddHttpContextAccessor();
            services.AddMemoryCache();
            services.Configure<Paths>(a =>
            {
                a.BridgeFolder = Configuration.GetValue<string>("BridgeFolder", "extensions");
                a.TempFolder = Configuration.GetValue<string>("TempFolder", string.Empty);
            });
            services.Configure<CacheOptions>(options =>
            {
                options.CachePath = Configuration.GetValue<string>("ThumbCacheFolder", "thumbs");
                options.AgeInDays = Configuration.GetValue<int>("CacheCheckInDays", 7);
            });

            services.AddExtensionsBridge();

            // Add consolidated services
            services.AddImportService();
            services.AddSeriesServices();
            services.AddJobServices();
            services.AddProviderServices();
            services.AddSearchServices();
            services.AddDownloadServices();
            services.AddHelperServices();
            services.AddBackgroundServices();

            // Register AppDbContext with SQLite provider, using the connection string from configuration (now points to runtime/kaizoku.db)
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(Configuration.GetConnectionString("DefaultConnection")));
            services.AddHostedService<StartupHostedService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseResponseCompression();
            app.UseSerilogRequestLogging();
            // Apply CORS policy before other middleware
            app.UseCors();

            // Allow both HTTP and HTTPS - UseHttpsRedirection is conditionally applied
            if (Configuration.GetValue("UseHttpsRedirection", false))
            {
                app.UseHttpsRedirection();
            }

            // Order matters for the following middleware
            app.UseRouting();

            // Configure static file serving with proper MIME types for .txt files
            var provider = new FileExtensionContentTypeProvider();
            // Add or update .txt mapping to ensure react/next.js fragments work
            provider.Mappings[".txt"] = "text/plain; charset=utf-8";

            // Serve default files (index.html)
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                DefaultFileNames = new List<string> { "index.html" },
                FileProvider = new PhysicalFileProvider(Path.Combine(EnvironmentSetup.Configuration!["runtimeDirectory"]!, "wwwroot"))
            });

            // Serve static files with custom content type provider
            app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = provider,
                ServeUnknownFileTypes = false, // Only serve files with known MIME types for security
                OnPrepareResponse = context =>
                {
                    // Add caching headers for static files
                    var headers = context.Context.Response.Headers;

                    // Cache .txt files for a shorter period (1 hour) since they might change more frequently
                    if (context.File.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        headers.CacheControl = "public, max-age=3600"; // 1 hour
                    }
                    // Cache other static files for longer (1 day)
                    else
                    {
                        headers.CacheControl = "public, max-age=86400"; // 24 hours
                    }
                }
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", context =>
                {
                    context.Response.Redirect("/library", permanent: false); // 302 Temporary
                    return Task.CompletedTask;
                });
                endpoints.MapControllers();
                endpoints.MapHub<ProgressHub>("/progress");
            });
            // Configure HSTS (HTTP Strict Transport Security)
            if (!env.IsDevelopment())
            {
                app.UseHsts(); // Adds HSTS header in production
            }

            Logger.LogInformation("Initializing Complete.");
        }
    }
}
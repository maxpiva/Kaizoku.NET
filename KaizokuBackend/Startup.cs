using KaizokuBackend.Data;
using KaizokuBackend.Hubs;
using KaizokuBackend.Services;
using KaizokuBackend.Services.Background;
using KaizokuBackend.Utils;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using Microsoft.Extensions.FileProviders;
using Polly;
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

            var retryPolicy = Policy<HttpResponseMessage>
                .Handle<Exception>()
                .OrResult(msg => !msg.IsSuccessStatusCode)
                .WaitAndRetryAsync(3,(attempt, result, _) =>
                    {
                        if (result?.Result?.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            return TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                        }
                        return TimeSpan.Zero; 
                    }, (_,_,_,_) => Task.CompletedTask);

            services.AddHttpClient<SuwayomiClient>().AddPolicyHandler(retryPolicy);

            // Add consolidated services
            services.AddImportService();
            services.AddSeriesServices();
            services.AddJobServices();
            services.AddProviderServices();
            services.AddSearchServices();
            services.AddDownloadServices();
            services.AddHelperServices();
            services.AddNamingServices();

            

            // Register AppDbContext with SQLite provider, using the connection string from configuration (now points to runtime/kaizoku.db)
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(Configuration.GetConnectionString("DefaultConnection")));
            // Register Suwayomi service as singleton to maintain the process lifetime
            services.AddSingleton<SuwayomiHostedService>();
            services.AddHostedService<StartupHostedService>();
            services.AddHostedService<JobScheduledHostedService>();
            services.AddHostedService<JobQueueHostedService>();
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

            var wwwrootPath = Path.Combine(EnvironmentSetup.Configuration!["runtimeDirectory"]!, "wwwroot");

            // Rewrite RSC payload requests: /{route}.txt -> /{route}/index.txt
            // Next.js 15 with trailingSlash: true generates RSC payloads at /{route}/index.txt
            // but the client requests them at /{route}.txt
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value;

                // Check if this is an RSC payload request (*.txt with _rsc query param)
                // and it's not already targeting index.txt or an API route
                if (path != null &&
                    path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith("/index.txt", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
                    context.Request.Query.ContainsKey("_rsc"))
                {
                    // Transform /route.txt to /route/index.txt
                    var routePath = path.Substring(0, path.Length - 4); // Remove .txt
                    var newPath = routePath + "/index.txt";

                    // Check if the rewritten file exists before rewriting
                    var physicalPath = Path.Combine(wwwrootPath, newPath.TrimStart('/'));
                    if (File.Exists(physicalPath))
                    {
                        context.Request.Path = newPath;
                    }
                }

                await next();
            });

            // Serve default files (index.html)
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                DefaultFileNames = new List<string> { "index.html" },
                FileProvider = new PhysicalFileProvider(wwwrootPath)
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
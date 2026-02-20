using Microsoft.OpenApi;
using System.Reflection;

namespace KaizokuBackend
{

    public static class OpenApiExtensions
    {
        /// <summary>
        /// Adds OpenAPI/Swagger configuration to the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddOpenApi(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Kaizoku.NET API",
                    Version = "v1",
                    Description = "Series backend API",
                    Contact = new OpenApiContact
                    {
                        Name = "Kaizoku.NET Team"
                    }
                });


                // Include XML comments if available
                var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath);
                }
            });

            return services;
        }


    }
}
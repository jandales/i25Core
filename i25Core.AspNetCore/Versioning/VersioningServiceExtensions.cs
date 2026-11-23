using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace i25Core.AspNetCore.Versioning
{
    public static class VersioningServiceExtensions
    {
        /// <summary>
        /// Turns on API versioning: /api/v1/..., /api/v2/..., the unversioned alias, version headers,
        /// one Swagger document per version and the standard error envelope for version errors.
        /// Call it before <c>AddProblemDetails()</c> so the error writer is consulted first.
        /// </summary>
        public static IServiceCollection AddI25ApiVersioning(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(ApiVersioningSettings.SectionName);
            services.Configure<ApiVersioningSettings>(section);
            var settings = section.Get<ApiVersioningSettings>() ?? new ApiVersioningSettings();

            // Fail at startup for a typo, not on the first request.
            if (!ApiVersionParser.Default.TryParse(settings.DefaultVersion, out var defaultVersion))
            {
                throw new InvalidOperationException(
                    $"ApiVersioning:DefaultVersion '{settings.DefaultVersion}' is not a valid version. Use a value like \"1.0\".");
            }

            var readers = new List<IApiVersionReader> { new UrlSegmentApiVersionReader() };

            if (settings.ReadFromHeader)
                readers.Add(new HeaderApiVersionReader(settings.HeaderName));

            // Registered first, so it is asked before the default problem-details writer.
            services.AddSingleton<IProblemDetailsWriter, ApiVersionProblemWriter>();

            services
                .AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = defaultVersion!;
                    options.AssumeDefaultVersionWhenUnspecified = settings.AssumeDefaultVersionWhenUnspecified;
                    options.ReportApiVersions = settings.ReportApiVersions;
                    options.ApiVersionReader = ApiVersionReader.Combine(readers.ToArray());
                })
                .AddMvc()
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";        // v1, v2, v1.1
                    options.SubstituteApiVersionInUrl = true;  // docs show /api/v1/... instead of /api/v{version}/...
                });

            services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

            return services;
        }

        /// <summary>Swagger UI with a drop-down listing every API version (newest first).</summary>
        public static WebApplication UseI25SwaggerUI(this WebApplication app)
        {
            var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

            app.UseSwaggerUI(options =>
            {
                foreach (var version in provider.ApiVersionDescriptions.Reverse())
                {
                    var name = version.IsDeprecated ? $"{version.GroupName.ToUpperInvariant()} (deprecated)" : version.GroupName.ToUpperInvariant();
                    options.SwaggerEndpoint($"/swagger/{version.GroupName}/swagger.json", name);
                }
            });

            return app;
        }
    }
}

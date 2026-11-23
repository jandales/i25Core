using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace i25Core.AspNetCore.Versioning
{
    /// <summary>Creates one Swagger document per API version (v1, v2, ...) and lists only the versioned URLs in them.</summary>
    public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
    {
        private readonly IApiVersionDescriptionProvider _versions;

        public ConfigureSwaggerOptions(IApiVersionDescriptionProvider versions)
        {
            _versions = versions;
        }

        public void Configure(SwaggerGenOptions options)
        {
            foreach (var version in _versions.ApiVersionDescriptions)
            {
                options.SwaggerDoc(version.GroupName, new OpenApiInfo
                {
                    Title = "i25Core API",
                    Version = version.ApiVersion.ToString(),
                    Description = version.IsDeprecated ? "This API version is deprecated. Please move to a newer version." : null
                });
            }

            // The unversioned alias (/api/Vendor) would repeat every operation in every document; show only /api/v{n}/...
            options.DocInclusionPredicate((documentName, api) =>
                api.GroupName == documentName &&
                api.RelativePath != null &&
                api.RelativePath.StartsWith("api/v", StringComparison.OrdinalIgnoreCase));

            options.OperationFilter<MarkDeprecatedVersionsFilter>();
        }
    }

    /// <summary>Swashbuckle only marks [Obsolete] operations as deprecated; also mark operations of a deprecated API version.</summary>
    public sealed class MarkDeprecatedVersionsFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.ApiDescription.IsDeprecated())
                operation.Deprecated = true;
        }
    }
}

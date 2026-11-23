using Microsoft.Extensions.Options;

namespace i25Core.AspNetCore.Security
{
    /// <summary>"Cors" section of appsettings: which browser front ends may call this API.</summary>
    public sealed class CorsSettings
    {
        public const string SectionName = "Cors";

        /// <summary>
        /// Exact origins such as "https://app.example.com" (scheme + host + optional port, no path, no trailing slash).
        /// Empty = CORS is off and only same-origin browser calls work. "*" = any origin (not allowed with credentials).
        /// </summary>
        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

        // The three lists below fall back to the built-in defaults when left out.
        // (They are null here on purpose: .NET configuration binding ADDS to an array that already has items.)

        public string[]? AllowedMethods { get; set; }
        public string[]? AllowedHeaders { get; set; }

        /// <summary>Response headers the browser's JavaScript is allowed to read.</summary>
        public string[]? ExposedHeaders { get; set; }

        /// <summary>Lets the browser send cookies / credentials. Needs explicit origins.</summary>
        public bool AllowCredentials { get; set; }

        /// <summary>How long the browser may remember a preflight answer.</summary>
        public int MaxAgeSeconds { get; set; } = 600;

        internal string[] Methods => AllowedMethods ?? new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
        internal string[] Headers => AllowedHeaders ?? new[] { "Authorization", "Content-Type", "Accept", "Accept-Language", "X-Locale", "X-Api-Version" };
        internal string[] Exposed => ExposedHeaders ?? new[] { "WWW-Authenticate", "Content-Language", "Retry-After" };
    }

    /// <summary>"SecurityHeaders" section of appsettings.</summary>
    public sealed class SecurityHeadersSettings
    {
        public const string SectionName = "SecurityHeaders";

        public bool Enabled { get; set; } = true;

        /// <summary>X-Frame-Options. Empty = not sent.</summary>
        public string FrameOptions { get; set; } = "DENY";

        public string ReferrerPolicy { get; set; } = "no-referrer";

        /// <summary>For a JSON API nothing should load or frame anything. Empty = not sent.</summary>
        public string ContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none'";

        /// <summary>Paths that get no Content-Security-Policy because their pages need scripts (Swagger UI, Hangfire dashboard).</summary>
        public string[]? ContentSecurityPolicySkipPaths { get; set; }

        public string PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=(), payment=()";
        public string CrossOriginOpenerPolicy { get; set; } = "same-origin";

        /// <summary>"same-site" lets a front end on a sibling subdomain embed files from the public disk; "same-origin" is stricter.</summary>
        public string CrossOriginResourcePolicy { get; set; } = "same-site";

        /// <summary>Stops browsers and proxies from storing sign-in and token responses.</summary>
        public bool NoStoreForAuthResponses { get; set; } = true;

        internal string[] CspSkipPaths => ContentSecurityPolicySkipPaths ?? new[] { "/swagger", "/jobs" };

        /// <summary>Kestrel's "Server: Kestrel" header.</summary>
        public bool RemoveServerHeader { get; set; } = true;

        public HstsSettings Hsts { get; set; } = new();
    }

    public sealed class HstsSettings
    {
        /// <summary>Sent only over HTTPS and never in Development.</summary>
        public bool Enabled { get; set; } = true;
        public int MaxAgeDays { get; set; } = 365;
        public bool IncludeSubDomains { get; set; }
        public bool Preload { get; set; }
    }

    public static class SecurityServiceExtensions
    {
        private const string PolicyName = "i25Core";

        public static IServiceCollection AddI25Security(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
        {
            var cors = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
            var headers = configuration.GetSection(SecurityHeadersSettings.SectionName).Get<SecurityHeadersSettings>() ?? new SecurityHeadersSettings();

            services.Configure<CorsSettings>(configuration.GetSection(CorsSettings.SectionName));
            services.Configure<SecurityHeadersSettings>(configuration.GetSection(SecurityHeadersSettings.SectionName));

            var origins = NormalizeOrigins(cors);

            if (origins.Length > 0)
            {
                services.AddCors(options => options.AddPolicy(PolicyName, policy =>
                {
                    if (origins.Contains("*"))
                        policy.AllowAnyOrigin();
                    else
                        policy.WithOrigins(origins);

                    policy.WithMethods(cors.Methods)
                          .WithHeaders(cors.Headers)
                          .WithExposedHeaders(cors.Exposed)
                          .SetPreflightMaxAge(TimeSpan.FromSeconds(Math.Max(0, cors.MaxAgeSeconds)));

                    if (cors.AllowCredentials)
                        policy.AllowCredentials();
                }));
            }

            if (headers.Hsts.Enabled)
            {
                services.AddHsts(options =>
                {
                    options.MaxAge = TimeSpan.FromDays(Math.Max(1, headers.Hsts.MaxAgeDays));
                    options.IncludeSubDomains = headers.Hsts.IncludeSubDomains;
                    options.Preload = headers.Hsts.Preload;
                });
            }

            return services;
        }

        /// <summary>Kestrel setting; call on the host builder before Build().</summary>
        public static WebApplicationBuilder ConfigureI25Security(this WebApplicationBuilder builder)
        {
            var headers = builder.Configuration.GetSection(SecurityHeadersSettings.SectionName).Get<SecurityHeadersSettings>()
                          ?? new SecurityHeadersSettings();

            if (headers.RemoveServerHeader)
                builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

            return builder;
        }

        /// <summary>
        /// First in the pipeline, so even error and 401 responses carry the headers. Also HSTS (non-Development).
        /// </summary>
        public static WebApplication UseI25SecurityHeaders(this WebApplication app)
        {
            var settings = app.Services.GetRequiredService<IOptions<SecurityHeadersSettings>>().Value;

            if (!settings.Enabled)
                return app;

            if (settings.Hsts.Enabled && !app.Environment.IsDevelopment())
                app.UseHsts();

            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    var h = context.Response.Headers;
                    var path = context.Request.Path;

                    // Add only when nobody set it already (the file storage middleware sets its own CSP, for example).
                    Set(h, "X-Content-Type-Options", "nosniff");
                    Set(h, "X-Frame-Options", settings.FrameOptions);
                    Set(h, "Referrer-Policy", settings.ReferrerPolicy);
                    Set(h, "Permissions-Policy", settings.PermissionsPolicy);
                    Set(h, "Cross-Origin-Opener-Policy", settings.CrossOriginOpenerPolicy);
                    Set(h, "Cross-Origin-Resource-Policy", settings.CrossOriginResourcePolicy);

                    if (!settings.CspSkipPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
                        Set(h, "Content-Security-Policy", settings.ContentSecurityPolicy);

                    if (settings.NoStoreForAuthResponses && IsAuthPath(path))
                    {
                        Set(h, "Cache-Control", "no-store");
                        Set(h, "Pragma", "no-cache");
                    }

                    return Task.CompletedTask;
                });

                await next();
            });

            return app;
        }

        /// <summary>CORS. Place it after the security headers and before authentication, so preflight requests are answered without a token.</summary>
        public static WebApplication UseI25Cors(this WebApplication app)
        {
            var cors = app.Services.GetRequiredService<IOptions<CorsSettings>>().Value;

            if (NormalizeOrigins(cors).Length > 0)
                app.UseCors(PolicyName);

            return app;
        }

        private static void Set(IHeaderDictionary headers, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !headers.ContainsKey(name))
                headers[name] = value;
        }

        // /api/Auth/..., /api/v1/Auth/...
        private static bool IsAuthPath(PathString path)
            => path.Value != null && path.Value.Contains("/Auth/", StringComparison.OrdinalIgnoreCase);

        private static string[] NormalizeOrigins(CorsSettings cors)
        {
            var result = new List<string>();

            foreach (var raw in cors.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)))
            {
                var origin = raw.Trim().TrimEnd('/');

                if (origin == "*")
                {
                    if (cors.AllowCredentials)
                        throw new InvalidOperationException("Cors: \"*\" cannot be combined with AllowCredentials. List the exact origins instead.");
                    result.Add(origin);
                    continue;
                }

                var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                            && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
                            && string.IsNullOrEmpty(uri.UserInfo);

                if (!valid)
                    throw new InvalidOperationException(
                        $"Cors:AllowedOrigins contains \"{raw}\". Use an origin like \"https://app.example.com\" (scheme, host and optional port; no path).");

                result.Add(origin);
            }

            if (result.Contains("*") && result.Count > 1)
                throw new InvalidOperationException("Cors:AllowedOrigins mixes \"*\" with specific origins. Use one or the other.");

            return result.ToArray();
        }
    }
}

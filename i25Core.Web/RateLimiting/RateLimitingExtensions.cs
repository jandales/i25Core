using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using i25Core.Core.DTO;
using i25Core.Core.Localization;

namespace i25Core.Web.RateLimiting
{
    public sealed class LimitRule
    {
        /// <summary>Requests allowed per window.</summary>
        public int PermitLimit { get; set; }

        /// <summary>Window length in seconds.</summary>
        public int WindowSeconds { get; set; }

        public LimitRule() { }
        public LimitRule(int permitLimit, int windowSeconds) { PermitLimit = permitLimit; WindowSeconds = windowSeconds; }
    }

    public sealed class GlobalLimitSettings
    {
        /// <summary>Callers that are not signed in, counted per client address.</summary>
        public LimitRule Anonymous { get; set; } = new(60, 60);

        /// <summary>Signed-in callers, counted per user.</summary>
        public LimitRule Authenticated { get; set; } = new(300, 60);
    }

    /// <summary>"RateLimiting" section of appsettings.</summary>
    public sealed class RateLimitSettings
    {
        public const string SectionName = "RateLimiting";

        public bool Enabled { get; set; } = true;

        public GlobalLimitSettings Global { get; set; } = new();

        /// <summary>
        /// Named limits for [EnableRateLimiting("name")]. "auth" (sign-in, register, password reset) and "strict" always
        /// exist; add your own names here. Counted per user when signed in, per client address otherwise ("auth": always per address).
        /// </summary>
        public Dictionary<string, LimitRule>? Policies { get; set; }

        /// <summary>Paths never limited (probes). Prefix match on whole path segments.</summary>
        public string[]? ExemptPaths { get; set; }

        /// <summary>The window is cut into this many parts; the limit slides smoothly instead of resetting at once.</summary>
        public int SegmentsPerWindow { get; set; } = 6;

        internal string[] Exempt => ExemptPaths ?? new[] { "/health" };

        // (.NET configuration adds to a dictionary or array that already has items, so defaults are applied here.)
        internal Dictionary<string, LimitRule> AllPolicies
        {
            get
            {
                var all = new Dictionary<string, LimitRule>(StringComparer.OrdinalIgnoreCase)
                {
                    ["auth"] = new LimitRule(10, 60),
                    ["strict"] = new LimitRule(5, 60)
                };
                if (Policies != null)
                    foreach (var (name, rule) in Policies)
                        all[name] = rule;
                return all;
            }
        }
    }

    /// <summary>"ForwardedHeaders" section: trust X-Forwarded-For / -Proto from your reverse proxy.</summary>
    public sealed class ForwardedHeadersSettings
    {
        public const string SectionName = "ForwardedHeaders";

        /// <summary>Turn on only when the app runs behind a proxy or load balancer, and list it below.</summary>
        public bool Enabled { get; set; }

        /// <summary>Addresses of your proxies, for example "10.0.0.5".</summary>
        public string[] KnownProxies { get; set; } = Array.Empty<string>();

        /// <summary>Address ranges of your proxies in CIDR form, for example "10.0.0.0/8".</summary>
        public string[] KnownNetworks { get; set; } = Array.Empty<string>();

        /// <summary>How many proxies in a row are skipped to find the client. 1 = one proxy in front.</summary>
        public int ForwardLimit { get; set; } = 1;
    }

    public static class RateLimitingExtensions
    {
        public const string RejectedPolicyItem = "RateLimitPolicy";

        public static IServiceCollection AddI25RateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            var settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>() ?? new RateLimitSettings();
            var proxy = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

            services.Configure<RateLimitSettings>(configuration.GetSection(RateLimitSettings.SectionName));
            services.Configure<ForwardedHeadersSettings>(configuration.GetSection(ForwardedHeadersSettings.SectionName));

            if (proxy.Enabled)
                services.Configure<ForwardedHeadersOptions>(options => ConfigureForwardedHeaders(options, proxy));

            if (!settings.Enabled)
                return services;

            Validate("Global:Anonymous", settings.Global.Anonymous);
            Validate("Global:Authenticated", settings.Global.Authenticated);
            foreach (var (name, rule) in settings.AllPolicies)
                Validate($"Policies:{name}", rule);
            if (settings.SegmentsPerWindow < 1)
                throw new InvalidOperationException("RateLimiting:SegmentsPerWindow must be at least 1.");

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Applies to every request that reaches the limiter. Probes are skipped.
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    if (settings.Exempt.Any(p => http.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
                        return RateLimitPartition.GetNoLimiter("exempt");

                    var userId = http.User.FindFirst("sub")?.Value;
                    return userId != null
                        ? Sliding($"user:{userId}", settings.Global.Authenticated, settings)
                        : Sliding($"ip:{ClientAddress(http)}", settings.Global.Anonymous, settings);
                });

                foreach (var (name, rule) in settings.AllPolicies)
                {
                    var perAddressOnly = name.Equals("auth", StringComparison.OrdinalIgnoreCase);

                    options.AddPolicy(name, http =>
                    {
                        // Sign-in endpoints are anonymous by nature, so they are always counted per address.
                        var userId = perAddressOnly ? null : http.User.FindFirst("sub")?.Value;
                        return Sliding(userId != null ? $"user:{userId}" : $"ip:{ClientAddress(http)}", rule, settings);
                    });
                }

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var http = context.HttpContext;

                    var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
                    http.Items[RejectedPolicyItem] = policy;

                    var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                        : 1;

                    var translator = http.RequestServices.GetRequiredService<ITranslator>();
                    var response = ApiResponse<object?>.Failure(translator.Get("errors.rate_limited", new { seconds }), null);
                    response.TraceId = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier;

                    http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    http.Response.Headers.RetryAfter = seconds.ToString();
                    await http.Response.WriteAsJsonAsync(response, cancellationToken);
                };
            });

            return services;
        }

        /// <summary>First in the pipeline, so everything after it (HTTPS redirect, logging, limits) sees the real client address.</summary>
        public static WebApplication UseI25ForwardedHeaders(this WebApplication app)
        {
            var proxy = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;

            if (proxy.Enabled)
                app.UseForwardedHeaders();

            return app;
        }

        /// <summary>After authentication (so signed-in callers are counted per user) and before authorization.</summary>
        public static WebApplication UseI25RateLimiting(this WebApplication app)
        {
            var settings = app.Services.GetRequiredService<IOptions<RateLimitSettings>>().Value;

            if (settings.Enabled)
                app.UseRateLimiter();

            return app;
        }

        private static RateLimitPartition<string> Sliding(string key, LimitRule rule, RateLimitSettings settings)
            => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                SegmentsPerWindow = settings.SegmentsPerWindow,
                QueueLimit = 0,                 // refuse at once, never hold the request
                AutoReplenishment = true
            });

        /// <summary>
        /// The client's address as a counting key. IPv6 clients get a whole /64 network to themselves, so one machine
        /// cannot dodge the limit by rotating addresses inside its own range.
        /// </summary>
        internal static string ClientAddress(HttpContext http)
        {
            var ip = http.Connection.RemoteIpAddress;

            if (ip == null)
                return "unknown";

            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = ip.GetAddressBytes();
                return Convert.ToHexString(bytes, 0, 8) + "/64";
            }

            return ip.ToString();
        }

        private static void Validate(string name, LimitRule rule)
        {
            if (rule.PermitLimit < 1 || rule.WindowSeconds < 1)
                throw new InvalidOperationException($"RateLimiting:{name} needs PermitLimit and WindowSeconds of 1 or more.");
        }

        private static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, ForwardedHeadersSettings proxy)
        {
            if (proxy.KnownProxies.Length == 0 && proxy.KnownNetworks.Length == 0)
                throw new InvalidOperationException(
                    "ForwardedHeaders is enabled but KnownProxies and KnownNetworks are empty. Without them any caller could send a fake " +
                    "X-Forwarded-For and pick its own address. List your proxy addresses.");

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = Math.Max(1, proxy.ForwardLimit);

            // Only the proxies you list are believed (the built-in "loopback is trusted" default is removed).
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var address in proxy.KnownProxies)
            {
                if (!IPAddress.TryParse(address.Trim(), out var ip))
                    throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains \"{address}\", which is not an IP address.");
                options.KnownProxies.Add(ip);
            }

            foreach (var network in proxy.KnownNetworks)
            {
                var parts = network.Trim().Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length)
                    || length < 0 || length > (prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128))
                    throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains \"{network}\". Use CIDR form like \"10.0.0.0/8\".");
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
            }
        }
    }
}

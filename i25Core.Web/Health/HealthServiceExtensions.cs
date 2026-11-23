using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using i25Core.Web.Auth;

namespace i25Core.Web.Health
{
    /// <summary>"HealthChecks" section of appsettings.</summary>
    public sealed class HealthCheckSettings
    {
        public const string SectionName = "HealthChecks";

        public bool Enabled { get; set; } = true;

        /// <summary>Base path: {Path}/live, {Path}/ready and {Path}/details (admin only). {Path} alone is the same as /ready.</summary>
        public string Path { get; set; } = "/health";

        /// <summary>Longest a single check may run before it counts as failed.</summary>
        public int TimeoutSeconds { get; set; } = 5;

        /// <summary>
        /// The anonymous /ready answer is reused for this long, so a probe (or anyone) calling it in a loop
        /// cannot turn it into load on the database. 0 = never reuse.
        /// </summary>
        public int CacheSeconds { get; set; } = 5;

        public bool Database { get; set; } = true;
        public bool Storage { get; set; } = true;
        public bool BackgroundJobs { get; set; } = true;
    }

    public static class HealthServiceExtensions
    {
        public const string ReadyTag = "ready";

        public static IServiceCollection AddI25HealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            var settings = configuration.GetSection(HealthCheckSettings.SectionName).Get<HealthCheckSettings>() ?? new HealthCheckSettings();
            services.Configure<HealthCheckSettings>(configuration.GetSection(HealthCheckSettings.SectionName));

            if (!settings.Enabled)
                return services;

            if (string.IsNullOrWhiteSpace(settings.Path) || !settings.Path.StartsWith('/') || settings.Path.Length > 1 && settings.Path.EndsWith('/'))
                throw new InvalidOperationException("HealthChecks:Path must start with / and must not end with / (for example \"/health\").");

            var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            var builder = services.AddHealthChecks();

            if (settings.Database) builder.AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, new[] { ReadyTag }, timeout);
            if (settings.Storage) builder.AddCheck<StorageHealthCheck>("storage", HealthStatus.Unhealthy, new[] { ReadyTag }, timeout);
            if (settings.BackgroundJobs) builder.AddCheck<BackgroundJobsHealthCheck>("background-jobs", HealthStatus.Unhealthy, new[] { ReadyTag }, timeout);

            services.AddSingleton<ReadyReportCache>();
            return services;
        }

        /// <summary>
        /// Adds your own check to the readiness group: <c>services.AddI25HealthCheck&lt;PaymentGatewayCheck&gt;("payment-gateway")</c>.
        /// </summary>
        public static IServiceCollection AddI25HealthCheck<T>(this IServiceCollection services, string name, int timeoutSeconds = 5)
            where T : class, IHealthCheck
        {
            services.AddHealthChecks().AddCheck<T>(name, HealthStatus.Unhealthy, new[] { ReadyTag }, TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
            return services;
        }

        public static WebApplication MapI25HealthChecks(this WebApplication app)
        {
            var settings = app.Configuration.GetSection(HealthCheckSettings.SectionName).Get<HealthCheckSettings>() ?? new HealthCheckSettings();

            if (!settings.Enabled)
                return app;

            var path = settings.Path;

            // Alive: the process answers. Checks nothing else, so a slow database never makes an orchestrator kill a healthy app.
            app.MapGet($"{path}/live", (HttpContext http) =>
            {
                NoStore(http);
                return Results.Json(new { status = "Healthy" });
            }).AllowAnonymous().ExcludeFromDescription();

            // Ready: can it do its job? Anonymous on purpose (load balancers call it) and says nothing except the overall status.
            var ready = async (HttpContext http, ReadyReportCache cache) =>
            {
                NoStore(http);
                var report = await cache.GetAsync(http.RequestAborted);
                return Results.Json(new { status = report.Status.ToString() }, statusCode: StatusFor(report.Status));
            };
            app.MapGet($"{path}/ready", ready).AllowAnonymous().ExcludeFromDescription();
            app.MapGet(path, ready).AllowAnonymous().ExcludeFromDescription();

            // Details: every check with timing and error text. Admins only, and never cached.
            app.MapGet($"{path}/details", async (HttpContext http, HealthCheckService service) =>
            {
                NoStore(http);
                var report = await service.CheckHealthAsync(r => r.Tags.Contains(ReadyTag), http.RequestAborted);

                return Results.Json(new
                {
                    status = report.Status.ToString(),
                    totalMs = Math.Round(report.TotalDuration.TotalMilliseconds),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        error = e.Value.Exception?.Message,
                        ms = Math.Round(e.Value.Duration.TotalMilliseconds)
                    })
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, statusCode: StatusFor(report.Status));
            }).RequireAuthorization(AuthServiceExtensions.AdminPolicy).ExcludeFromDescription();

            return app;
        }

        // Degraded still answers 200: the instance works, so it must stay in rotation.
        private static int StatusFor(HealthStatus status) => status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;

        private static void NoStore(HttpContext http)
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
        }
    }

    /// <summary>Keeps the last readiness report for a few seconds. One run at a time.</summary>
    public sealed class ReadyReportCache
    {
        private readonly HealthCheckService _service;
        private readonly TimeSpan _ttl;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private HealthReport? _report;
        private DateTime _takenAt;

        public ReadyReportCache(HealthCheckService service, Microsoft.Extensions.Options.IOptions<HealthCheckSettings> settings)
        {
            _service = service;
            _ttl = TimeSpan.FromSeconds(Math.Max(0, settings.Value.CacheSeconds));
        }

        public async Task<HealthReport> GetAsync(CancellationToken cancellationToken)
        {
            if (Fresh()) return _report!;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                // Someone else may have refreshed it while we waited.
                if (Fresh()) return _report!;

                _report = await _service.CheckHealthAsync(r => r.Tags.Contains(HealthServiceExtensions.ReadyTag), cancellationToken);
                _takenAt = DateTime.UtcNow;
                return _report;
            }
            finally
            {
                _gate.Release();
            }
        }

        private bool Fresh() => _report != null && _ttl > TimeSpan.Zero && DateTime.UtcNow - _takenAt < _ttl;
    }
}

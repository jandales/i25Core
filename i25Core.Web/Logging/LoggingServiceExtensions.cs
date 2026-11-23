using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace i25Core.Web.Logging
{
    /// <summary>"StructuredLogging" section of appsettings. Log levels live in the standard "Serilog" section.</summary>
    public sealed class StructuredLoggingSettings
    {
        public const string SectionName = "StructuredLogging";

        /// <summary>"Auto" = readable text in Development, one JSON object per line everywhere else. Or "Text" / "Json".</summary>
        public string ConsoleFormat { get; set; } = "Auto";

        public FileLogSettings File { get; set; } = new();
        public RequestLogSettings Requests { get; set; } = new();
    }

    public sealed class FileLogSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Daily files: "logs/log-.json" becomes logs/log-20261123.json. Relative paths start at the app folder.</summary>
        public string Path { get; set; } = "logs/log-.json";

        public int RetainedDays { get; set; } = 14;

        /// <summary>A file that reaches this size starts a new one (log-20261123_001.json). 0 = no limit.</summary>
        public int MaxFileSizeMb { get; set; } = 100;
    }

    public sealed class RequestLogSettings
    {
        /// <summary>One summary line per HTTP request.</summary>
        public bool Enabled { get; set; } = true;

        public string CorrelationIdHeader { get; set; } = "X-Correlation-Id";

        /// <summary>Requests slower than this are logged as Warning. 0 = off.</summary>
        public int SlowRequestMs { get; set; } = 2000;

        /// <summary>Paths that are logged only at Debug (probes would otherwise fill the log).</summary>
        public string[]? QuietPaths { get; set; }

        internal string[] Quiet => QuietPaths ?? new[] { "/health", "/jobs", "/swagger", "/favicon.ico" };
    }

    public static class LoggingServiceExtensions
    {
        public const string ApplicationName = "i25Core";

        /// <summary>
        /// Replaces the default logging with Serilog: structured events, JSON output, a rolling file, and the
        /// levels from the "Serilog" section of appsettings. Existing <c>ILogger&lt;T&gt;</c> calls keep working.
        /// </summary>
        public static WebApplicationBuilder AddI25Logging(this WebApplicationBuilder builder)
        {
            var settings = builder.Configuration.GetSection(StructuredLoggingSettings.SectionName).Get<StructuredLoggingSettings>()
                           ?? new StructuredLoggingSettings();

            if (!new[] { "auto", "text", "json" }.Contains(settings.ConsoleFormat?.ToLowerInvariant()))
                throw new InvalidOperationException("StructuredLogging:ConsoleFormat must be Auto, Text or Json.");

            builder.Services.Configure<StructuredLoggingSettings>(builder.Configuration.GetSection(StructuredLoggingSettings.SectionName));

            builder.Host.UseSerilog((context, services, logger) =>
            {
                logger
                    .ReadFrom.Configuration(context.Configuration)          // levels and overrides ("Serilog" section)
                    .Enrich.FromLogContext()                                // CorrelationId, UserId, ...
                    .Enrich.WithProperty("Application", ApplicationName)
                    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

                var json = settings.ConsoleFormat!.Equals("json", StringComparison.OrdinalIgnoreCase)
                           || settings.ConsoleFormat.Equals("auto", StringComparison.OrdinalIgnoreCase) && !context.HostingEnvironment.IsDevelopment();

                if (json)
                    logger.WriteTo.Console(new RenderedCompactJsonFormatter());
                else
                    logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

                if (settings.File.Enabled)
                {
                    var limit = settings.File.MaxFileSizeMb > 0 ? settings.File.MaxFileSizeMb * 1024L * 1024L : (long?)null;
                    logger.WriteTo.File(
                        new RenderedCompactJsonFormatter(),
                        Path.Combine(context.HostingEnvironment.ContentRootPath, settings.File.Path),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: Math.Max(1, settings.File.RetainedDays),
                        fileSizeLimitBytes: limit,
                        rollOnFileSizeLimit: limit.HasValue);
                }
            });

            return builder;
        }

        /// <summary>
        /// Puts a correlation id on every request: taken from the request header when it looks safe, otherwise generated.
        /// It is returned in the response header and attached to every log line written during the request.
        /// Place it first.
        /// </summary>
        public static WebApplication UseI25CorrelationId(this WebApplication app)
        {
            var header = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StructuredLoggingSettings>>().Value.Requests.CorrelationIdHeader;
            var safe = new Regex("^[A-Za-z0-9._-]{8,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

            app.Use(async (context, next) =>
            {
                var incoming = context.Request.Headers[header].ToString();
                // A client-supplied value goes into logs, so only plain, short values are accepted (no log forging).
                var id = !string.IsNullOrEmpty(incoming) && safe.IsMatch(incoming)
                    ? incoming
                    : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

                context.Items["CorrelationId"] = id;
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[header] = id;
                    return Task.CompletedTask;
                });

                using (LogContext.PushProperty("CorrelationId", id))
                    await next();
            });

            return app;
        }

        /// <summary>One summary line per request: method, path, status, time, user, client address.</summary>
        public static WebApplication UseI25RequestLogging(this WebApplication app)
        {
            var settings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StructuredLoggingSettings>>().Value.Requests;

            if (!settings.Enabled)
                return app;

            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms";

                options.GetLevel = (http, elapsedMs, exception) =>
                {
                    if (exception != null || http.Response.StatusCode >= 500) return LogEventLevel.Error;
                    if (settings.Quiet.Any(p => http.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))) return LogEventLevel.Debug;
                    if (settings.SlowRequestMs > 0 && elapsedMs > settings.SlowRequestMs) return LogEventLevel.Warning;
                    return LogEventLevel.Information;
                };

                // Only the path is logged, never the query string (it can carry tokens), headers or bodies.
                options.EnrichDiagnosticContext = (diagnostic, http) =>
                {
                    diagnostic.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
                    diagnostic.Set("UserId", http.User.FindFirst("sub")?.Value);

                    var agent = http.Request.Headers.UserAgent.ToString();
                    if (agent.Length > 0)
                        diagnostic.Set("UserAgent", agent.Length > 120 ? agent[..120] : agent);
                };
            });

            return app;
        }

        /// <summary>After authentication: every log line written while the request runs also carries the user id.</summary>
        public static WebApplication UseI25LogUser(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                var userId = context.User.FindFirst("sub")?.Value;

                if (userId == null)
                {
                    await next();
                    return;
                }

                using (LogContext.PushProperty("UserId", userId))
                    await next();
            });

            return app;
        }
    }
}

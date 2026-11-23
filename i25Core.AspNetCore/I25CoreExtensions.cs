using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Serilog;
using i25Core.AspNetCore.Auth;
using i25Core.AspNetCore.BackgroundJobs;
using i25Core.AspNetCore.Caching;
using i25Core.AspNetCore.Events;
using i25Core.AspNetCore.FileStorage;
using i25Core.AspNetCore.Health;
using i25Core.AspNetCore.Localization;
using i25Core.AspNetCore.Logging;
using i25Core.AspNetCore.Mail;
using i25Core.AspNetCore.Middleware;
using i25Core.AspNetCore.RateLimiting;
using i25Core.AspNetCore.Security;
using i25Core.AspNetCore.Validator.Extensions;
using i25Core.AspNetCore.Versioning;
using i25Core.BusinessLayer.Implementation;
using i25Core.BusinessLayer.Implementation.Base;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.Core.DTO;
using i25Core.Core.Localization;
using i25Core.Infrastructure;
using i25Core.Infrastructure.Extensions;

namespace i25Core.AspNetCore
{
    /// <summary>What <see cref="I25CoreExtensions.AddI25Core{TContext}"/> may be told.</summary>
    public sealed class I25CoreOptions
    {
        /// <summary>Name of the connection string under "ConnectionStrings".</summary>
        public string ConnectionStringName { get; set; } = "DefaultConnection";

        /// <summary>
        /// Register the built-in controllers: Auth, Users, Roles, AuditLog, Locale and Notification.
        /// Switch off when you only want the services and write your own controllers.
        /// </summary>
        public bool IncludeControllers { get; set; } = true;

        /// <summary>Swagger and Swagger UI (Swagger UI only in Development).</summary>
        public bool EnableSwagger { get; set; } = true;

        /// <summary>
        /// Assemblies scanned for FluentValidation validators and for event listeners (<c>IEventListener&lt;T&gt;</c>), in addition
        /// to the library itself. Defaults to the application's entry assembly.
        /// </summary>
        public List<Assembly> ScanAssemblies { get; } = new();
    }

    public static class I25CoreExtensions
    {
        /// <summary>
        /// Registers everything i25Core offers: logging, database, validation, background jobs, mail and notifications,
        /// caching, events, file storage, controllers and Swagger, API versioning, error handling, localization,
        /// authentication, security headers and CORS, health checks and rate limiting.
        /// Configure each part in appsettings.json. Call <see cref="UseI25Core"/> after <c>Build()</c>.
        /// </summary>
        public static WebApplicationBuilder AddI25Core(this WebApplicationBuilder builder, Action<I25CoreOptions>? configure = null)
            => builder.AddI25Core<I25DbContext>(configure);

        /// <summary>The same, with your own database context (a class that inherits <see cref="I25DbContext"/>).</summary>
        public static WebApplicationBuilder AddI25Core<TContext>(this WebApplicationBuilder builder, Action<I25CoreOptions>? configure = null)
            where TContext : I25DbContext
        {
            var options = new I25CoreOptions();
            configure?.Invoke(options);

            var scan = options.ScanAssemblies.Count > 0
                ? options.ScanAssemblies.ToList()
                : new List<Assembly> { Assembly.GetEntryAssembly() ?? typeof(I25CoreExtensions).Assembly };

            var services = builder.Services;
            var configuration = builder.Configuration;
            var environment = builder.Environment;

            var connectionString = configuration.GetConnectionString(options.ConnectionStringName);

            builder.AddI25Logging();          // structured logs (Serilog): console, JSON, rolling file
            builder.ConfigureI25Security();   // removes the "Server: Kestrel" header

            services.AddI25Infrastructure<TContext>(connectionString!);

            services.AddScoped<IRepository, BaseRepository>();
            services.AddScoped(typeof(IBusinessLayer<>), typeof(BaseBusinessLayer<>));
            services.AddScoped<IAuditLogBusinessLayer, AuditLogBusinessLayerLayer>();

            // Validators: the library's own plus the application's.
            services.AddFoundationValidators(typeof(I25CoreExtensions).Assembly);
            foreach (var assembly in scan.Distinct())
                services.AddFoundationValidators(assembly);

            // Keep automatic [ApiController] model validation responses in the standard API response envelope.
            services.PostConfigure<ApiBehaviorOptions>(api =>
            {
                api.InvalidModelStateResponseFactory = context =>
                {
                    var translator = context.HttpContext.RequestServices.GetRequiredService<ITranslator>();

                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value!.Errors
                                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                    ? translator.Get("errors.invalid_value")
                                    : error.ErrorMessage)
                                .ToArray());

                    return new BadRequestObjectResult(ApiResponse<object?>.Failure(translator.Get("errors.validation_failed"), errors));
                };
            });

            services.AddI25BackgroundJobs(configuration, connectionString!);
            services.AddI25Mail(configuration, environment);
            services.AddI25Notifications();
            services.AddI25Caching(configuration);
            services.AddI25Events(configuration, scan.Append(typeof(I25CoreExtensions).Assembly).Distinct().ToArray());
            services.AddI25Storage(configuration, environment);

            var mvc = services.AddControllers();
            var library = typeof(I25CoreExtensions).Assembly;

            mvc.ConfigureApplicationPartManager(manager =>
            {
                var part = manager.ApplicationParts.OfType<AssemblyPart>().FirstOrDefault(p => p.Assembly == library);

                if (options.IncludeControllers && part == null)
                    manager.ApplicationParts.Add(new AssemblyPart(library));
                else if (!options.IncludeControllers && part != null)
                    manager.ApplicationParts.Remove(part);
            });

            if (options.EnableSwagger)
            {
                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen();
            }

            // Must come before AddProblemDetails so version errors use the standard ApiResponse envelope.
            services.AddI25ApiVersioning(configuration);

            services.AddExceptionHandler<GlobalExceptionHandler>();
            services.AddProblemDetails();

            services.AddI25Localization(configuration, environment);
            services.AddI25Auth(configuration, environment);
            services.AddI25Security(configuration, environment);
            services.AddI25HealthChecks(configuration);
            services.AddI25RateLimiting(configuration);

            builder.Services.AddSingleton(options);   // so UseI25Core knows what was switched on

            return builder;
        }

        /// <summary>
        /// The request pipeline in the right order (proxy headers, security headers, correlation id and request log,
        /// language, error handling, CORS, Swagger, HTTPS, file storage, job dashboard, authentication, rate limits,
        /// authorization, output cache) and the endpoints (controllers and health checks).
        /// Add your own endpoints after it.
        /// </summary>
        public static WebApplication UseI25Core(this WebApplication app)
        {
            var options = app.Services.GetRequiredService<I25CoreOptions>();

            // Before everything else, so the HTTPS redirect, logs and rate limits see the real client address (only behind a listed proxy).
            app.UseI25ForwardedHeaders();

            // Very first after that, so every response (errors and 401s included) carries the security headers.
            app.UseI25SecurityHeaders();

            app.UseI25CorrelationId();
            app.UseI25RequestLogging();

            // The request's language must be known to everything after it, including error messages.
            app.UseI25Localization();

            app.UseExceptionHandler();

            // Before the HTTPS redirect and before authentication: a browser preflight (OPTIONS) has no token and cannot follow a redirect.
            app.UseI25Cors();

            if (options.EnableSwagger && app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseI25SwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseI25Storage();

            // The Hangfire dashboard must come BEFORE authorization: the secure-by-default policy applies to every
            // request that reaches UseAuthorization, and the dashboard has its own access rule.
            app.UseI25BackgroundJobs();

            app.UseI25Auth();
            app.UseI25RateLimiting();   // after authentication: signed-in callers are counted per user
            app.UseI25LogUser();        // the user id goes on every log line of the rest of the request
            app.UseAuthorization();

            app.UseOutputCache();

            app.MapControllers();
            app.MapI25HealthChecks();

            return app;
        }
    }

    /// <summary>The whole application in one call, for a Program.cs that needs nothing special.</summary>
    public static class I25CoreApp
    {
        /// <summary>
        /// Starts the application: a bootstrap logger (so startup errors are structured), <c>AddI25Core</c>, your
        /// <paramref name="configureServices"/>, <c>UseI25Core</c>, your <paramref name="configurePipeline"/>, then Run.
        /// Returns the process exit code (0, or 1 when startup failed).
        /// </summary>
        public static int Run<TContext>(
            string[] args,
            Action<WebApplicationBuilder>? configureServices = null,
            Action<WebApplication>? configurePipeline = null,
            Action<I25CoreOptions>? configureOptions = null)
            where TContext : I25DbContext
        {
            // Logs written before the host exists (configuration errors, a missing JWT key) still come out as structured events.
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

            try
            {
                var builder = WebApplication.CreateBuilder(args);

                builder.AddI25Core<TContext>(o =>
                {
                    // The application that called Run is scanned for validators and event listeners.
                    if (Assembly.GetEntryAssembly() is { } entry)
                        o.ScanAssemblies.Add(entry);
                    configureOptions?.Invoke(o);
                });

                configureServices?.Invoke(builder);

                var app = builder.Build();
                app.UseI25Core();
                configurePipeline?.Invoke(app);

                app.Run();
                return 0;
            }
            catch (Exception ex) when (ex is not HostAbortedException)   // EF's design-time tools stop the host on purpose
            {
                Log.Fatal(ex, "The application stopped during startup");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary><see cref="Run{TContext}"/> with the standard i25Core database context.</summary>
        public static int Run(
            string[] args,
            Action<WebApplicationBuilder>? configureServices = null,
            Action<WebApplication>? configurePipeline = null,
            Action<I25CoreOptions>? configureOptions = null)
            => Run<I25DbContext>(args, configureServices, configurePipeline, configureOptions);
    }
}

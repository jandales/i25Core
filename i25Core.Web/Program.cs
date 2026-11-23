using i25Core.BusinessLayer.Implementation;
using i25Core.BusinessLayer.Implementation.Base;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.Infrastructure.Extensions;
using i25Core.Core.DTO;
using i25Core.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using i25Core.Web.Validator.Extensions;
using i25Core.Web.Validator.Base;
using i25Core.Web.Middleware;
using i25Core.Web.FileStorage;
using i25Core.Web.Mail;
using i25Core.Web.BackgroundJobs;
using i25Core.Web.Caching;
using i25Core.Web.Events;
using i25Core.Web.Versioning;
using i25Core.Web.Localization;
using i25Core.Core.Localization;
using i25Core.Web.Auth;
using i25Core.Web.Health;
using i25Core.Web.Logging;
using i25Core.Web.RateLimiting;
using Serilog;
using i25Core.Web.Security;

// Logs written before the host exists (configuration errors, a missing JWT key) still come out as structured events.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddI25Logging();          // structured logs (Serilog): console, JSON, rolling file. See "StructuredLogging" in appsettings.
    builder.ConfigureI25Security();   // removes the "Server: Kestrel" header

    // Add services to the container.

    // Add your infrastructure services here:
    builder.Services.AddI25Infrastructure(builder.Configuration.GetConnectionString("DefaultConnection")!);

    builder.Services.AddScoped<IRepository, BaseRepository>();
    builder.Services.AddScoped(typeof(IBusinessLayer<>), typeof(BaseBusinessLayer<>));
    builder.Services.AddScoped<IAuditLogBusinessLayer, AuditLogBusinessLayerLayer>();


    //Validators
    builder.Services.AddFoundationValidators(typeof(BaseValidator).Assembly);

    // Keep automatic [ApiController] model validation responses consistent
    // with the application's standard API response envelope.
    builder.Services.PostConfigure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
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

            var response = ApiResponse<object?>.Failure(
                translator.Get("errors.validation_failed"),
                errors);

            return new BadRequestObjectResult(response);
        };
    });

    // Background jobs + scheduler (Hangfire, stored in SQL Server). See "BackgroundJobs" in appsettings.
    builder.Services.AddI25BackgroundJobs(
        builder.Configuration,
        builder.Configuration.GetConnectionString("DefaultConnection")!);

    // Recurring jobs are registered here, e.g.:
    // builder.Services.AddRecurringJob<AuditCleanupJob>();

    // Mail (Smtp / Log / None driver) and notifications (mail + database channels). See "Mail" in appsettings.
    builder.Services.AddI25Mail(builder.Configuration, builder.Environment);
    builder.Services.AddI25Notifications();

    // Caching: ICacheService + [CacheResponse] response caching (memory or Redis, see "Cache" in appsettings).
    builder.Services.AddI25Caching(builder.Configuration);

    // Events + listeners: every IEventListener<T> in this assembly is registered automatically.
    builder.Services.AddI25Events(builder.Configuration, typeof(Program).Assembly);

    // File storage: IFileStorage / IFileStorageManager, named disks (local, public) and the upload helper. See "Storage" in appsettings.
    builder.Services.AddI25Storage(builder.Configuration, builder.Environment);

    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // API versioning: /api/v1/..., /api/v2/..., one Swagger document per version. See "ApiVersioning" in appsettings.
    // Must come before AddProblemDetails so version errors use the standard ApiResponse envelope.
    builder.Services.AddI25ApiVersioning(builder.Configuration);

    // Global exception handling: every unhandled exception becomes an ApiResponse.
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // Localization: ITranslator and the lang/ folder. See "Localization" in appsettings.
    builder.Services.AddI25Localization(builder.Configuration, builder.Environment);

    // Authentication + roles: JWT access tokens, refresh tokens, the current user. See "Auth" in appsettings.
    builder.Services.AddI25Auth(builder.Configuration, builder.Environment);

    // CORS (which browser front ends may call the API) and security headers. See "Cors" and "SecurityHeaders" in appsettings.
    builder.Services.AddI25Security(builder.Configuration, builder.Environment);

    // Health checks: /health/live, /health/ready and (admin only) /health/details. See "HealthChecks" in appsettings.
    builder.Services.AddI25HealthChecks(builder.Configuration);

    // Rate limiting (too many requests -> 429) and trusted proxy headers. See "RateLimiting" and "ForwardedHeaders" in appsettings.
    builder.Services.AddI25RateLimiting(builder.Configuration);

    var app = builder.Build();

    // Before everything else, so the HTTPS redirect, logs and rate limits see the real client address (only when behind a listed proxy).
    app.UseI25ForwardedHeaders();

    // Very first, so every response (errors and 401s included) carries the security headers.
    app.UseI25SecurityHeaders();

    // Correlation id for every request, then one summary log line per request.
    app.UseI25CorrelationId();
    app.UseI25RequestLogging();

    // First, so the request's language is known to everything after it, including error messages.
    app.UseI25Localization();

    app.UseExceptionHandler();

    // Before the HTTPS redirect and before authentication: a browser preflight (OPTIONS) has no token and cannot follow a redirect.
    app.UseI25Cors();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseI25SwaggerUI();
    }

    app.UseHttpsRedirection();

    // Serves the public disk (default: files under /storage). Private disks are never served.
    app.UseI25Storage();

    // Hangfire dashboard at /jobs (local machine only). It must come BEFORE authorization: the secure-by-default
    // policy applies to every request that reaches UseAuthorization, and the dashboard has its own access rule.
    app.UseI25BackgroundJobs();

    // Who is calling? (reads the bearer token), then may they? (roles and [Authorize]).
    app.UseI25Auth();
    app.UseI25RateLimiting();   // after authentication: signed-in callers are counted per user
    app.UseI25LogUser();     // the user id goes on every log line of the rest of the request
    app.UseAuthorization();

    app.UseOutputCache();

    app.MapControllers();
    app.MapI25HealthChecks();

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "The application stopped during startup");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

using i25Core.Core.Auth;
using i25Core.Infrastructure.Auth;
using i25Core.AspNetCore.BackgroundJobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace i25Core.AspNetCore.Auth
{
    public static class AuthServiceExtensions
    {
        /// <summary>The name of the policy that requires the Admin role: <c>[Authorize(Policy = AuthServiceExtensions.AdminPolicy)]</c>.</summary>
        public const string AdminPolicy = "AdminOnly";

        /// <summary>
        /// Registers sign-in with JWT access tokens and refresh tokens, roles, the current-user service,
        /// the 401/403 envelope, Swagger's Authorize button and the first-admin bootstrap.
        /// Settings come from the "Auth" section of appsettings.json.
        /// </summary>
        public static IServiceCollection AddI25Auth(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            var section = configuration.GetSection(AuthOptions.SectionName);
            services.Configure<AuthOptions>(section);
            var settings = section.Get<AuthOptions>() ?? new AuthOptions();

            // Fail when deploying, not on the first sign-in.
            if (!environment.IsDevelopment() && (settings.Jwt.Key?.Length ?? 0) < JwtSigningKey.MinimumBytes)
            {
                throw new InvalidOperationException(
                    "Auth:Jwt:Key is missing or shorter than 32 characters. Set a random secret, for example with the " +
                    "environment variable Auth__Jwt__Key or user secrets. Never commit it to Git.");
            }

            services.AddHttpContextAccessor();

            services.AddSingleton<JwtSigningKey>();
            services.AddSingleton<ITokenService, JwtTokenService>();
            services.AddSingleton<PasswordService>();
            services.AddScoped<ICurrentUser, HttpCurrentUser>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserAdminService, UserAdminService>();

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer();

            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<JwtSigningKey, IOptions<AuthOptions>>((bearer, key, auth) =>
                {
                    var jwt = auth.Value.Jwt;

                    bearer.MapInboundClaims = false;              // keep "sub", "role" as written in the token
                    bearer.RequireHttpsMetadata = false;          // there is no metadata endpoint: the key is shared, not discovered
                    bearer.SaveToken = false;

                    bearer.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key.Key,
                        RequireSignedTokens = true,
                        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },   // blocks "alg: none" and algorithm-swap tricks

                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,

                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),

                        NameClaimType = "name",
                        RoleClaimType = "role"
                    };

                    bearer.Events = new JwtBearerEvents
                    {
                        // Remember why a token was refused, so the 401 can say "expired" (the client should refresh) or "invalid".
                        OnAuthenticationFailed = context =>
                        {
                            if (context.Exception is SecurityTokenExpiredException)
                                context.HttpContext.Items[ApiAuthorizationResultHandler.TokenFailureItem] = "expired";

                            return Task.CompletedTask;
                        }
                    };
                });

            services.AddAuthorization(options =>
            {
                options.AddPolicy(AdminPolicy, policy => policy.RequireRole(Roles.Admin));

                // Secure by default: an endpoint nobody thought about is closed, not open.
                if (settings.RequireAuthenticatedByDefault)
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build();
                }
            });

            services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();
            services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerAuth>();

            services.AddHostedService<BootstrapAdminHostedService>();
            services.AddRecurringJob<AuthCleanupJob>();

            return services;
        }

        /// <summary>Reads the bearer token of each request and signs the user in. Call it before UseAuthorization.</summary>
        public static WebApplication UseI25Auth(this WebApplication app)
        {
            // Create the signing key now, so a missing or short key (or the Development warning) shows at startup.
            app.Services.GetRequiredService<JwtSigningKey>();

            app.UseAuthentication();
            return app;
        }
    }

    /// <summary>Adds the "Authorize" button to Swagger, so you can paste a token and try protected endpoints.</summary>
    public sealed class ConfigureSwaggerAuth : IConfigureOptions<SwaggerGenOptions>
    {
        public void Configure(SwaggerGenOptions options)
        {
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Paste the access token from /api/Auth/login (just the token, without the word Bearer).",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };

            options.AddSecurityDefinition("Bearer", scheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
        }
    }
}

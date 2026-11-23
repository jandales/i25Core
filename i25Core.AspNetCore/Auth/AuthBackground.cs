using i25Core.Core.Auth;
using i25Core.Core.BackgroundJobs;
using i25Core.Infrastructure;
using i25Core.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace i25Core.AspNetCore.Auth
{
    /// <summary>
    /// Creates the first administrator at startup, when Auth:BootstrapAdmin:Email and :Password are set
    /// and no administrator exists yet. After that it does nothing, so it is safe to leave configured,
    /// but remove the password from your configuration once you have signed in.
    /// </summary>
    public sealed class BootstrapAdminHostedService : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly AuthOptions _options;
        private readonly ILogger<BootstrapAdminHostedService> _logger;

        public BootstrapAdminHostedService(
            IServiceProvider services,
            IOptions<AuthOptions> options,
            ILogger<BootstrapAdminHostedService> logger)
        {
            _services = services;
            _options = options.Value;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var admin = _options.BootstrapAdmin;

            if (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrWhiteSpace(admin.Password))
                return;

            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<I25DbContext>();

                if (await db.Users.AnyAsync(u => u.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin), cancellationToken))
                    return;

                var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

                var created = await users.CreateUserAsync(new CreateUserRequest
                {
                    Email = admin.Email,
                    Password = admin.Password,
                    Name = string.IsNullOrWhiteSpace(admin.Name) ? "Administrator" : admin.Name,
                    Roles = new List<string> { Roles.Admin, Roles.User }
                }, cancellationToken);

                _logger.LogWarning(
                    "The first administrator was created: {Email}. Remove Auth:BootstrapAdmin:Password from your configuration now.",
                    created.Email);
            }
            catch (Exception ex)
            {
                // Don't stop the app (the database may not be migrated yet), but make it loud.
                _logger.LogError(ex, "The first administrator could not be created from Auth:BootstrapAdmin.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Every night, deletes refresh tokens and password-reset tokens that can never be used again.</summary>
    public sealed class AuthCleanupJob : IRecurringJob
    {
        private readonly I25DbContext _db;
        private readonly ILogger<AuthCleanupJob> _logger;

        public AuthCleanupJob(I25DbContext db, ILogger<AuthCleanupJob> logger)
        {
            _db = db;
            _logger = logger;
        }

        public string Cron => "0 3 * * *";          // every day at 03:00 UTC

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            // Kept a little after they end, so a reuse of an old token is still recognised as reuse.
            var refresh = await _db.RefreshTokens
                .Where(t => t.ExpiresAt < now.AddDays(-1)
                         || (t.UsedAt != null && t.UsedAt < now.AddDays(-7))
                         || (t.RevokedAt != null && t.RevokedAt < now.AddDays(-7)))
                .ExecuteDeleteAsync(cancellationToken);

            var reset = await _db.PasswordResetTokens
                .Where(t => t.ExpiresAt < now.AddDays(-1) || (t.UsedAt != null && t.UsedAt < now.AddDays(-1)))
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation("Auth cleanup removed {Refresh} refresh tokens and {Reset} reset tokens.", refresh, reset);
        }
    }
}

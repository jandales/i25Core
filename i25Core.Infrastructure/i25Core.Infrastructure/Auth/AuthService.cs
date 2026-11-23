using System.Net;
using i25Core.Core.Auth;
using i25Core.Core.Entities;
using i25Core.Core.Exceptions;
using i25Core.Core.Localization;
using i25Core.Core.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Auth
{
    public class AuthService : IAuthService
    {
        private readonly I25DbContext _db;
        private readonly PasswordService _passwords;
        private readonly ITokenService _tokens;
        private readonly IMailer _mailer;
        private readonly ITranslator _translator;
        private readonly AuthOptions _options;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            I25DbContext db,
            PasswordService passwords,
            ITokenService tokens,
            IMailer mailer,
            ITranslator translator,
            IOptions<AuthOptions> options,
            ILogger<AuthService> logger)
        {
            _db = db;
            _passwords = passwords;
            _tokens = tokens;
            _mailer = mailer;
            _translator = translator;
            _options = options.Value;
            _logger = logger;
        }

        // ---------- sign up / sign in ----------

        public async Task<UserProfile> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        {
            if (!_options.AllowRegistration)
                throw new ForbiddenException("Registration is closed.").WithKey("auth.registration_closed");

            var email = CleanEmail(request.Email);
            _passwords.EnsureStrong(request.Password, email);

            if (await _db.Users.AnyAsync(u => u.NormalizedEmail == Normalize(email), cancellationToken))
                throw new ConflictException("An account with this email already exists.").WithKey("auth.email_taken");

            var user = new User
            {
                Email = email,
                NormalizedEmail = Normalize(email),
                Name = request.Name.Trim(),
                IsActive = true
            };

            user.PasswordHash = _passwords.Hash(user, request.Password);

            // Everyone starts with the User role.
            var userRole = await _db.Roles.FirstAsync(r => r.Name == Roles.User, cancellationToken);
            user.UserRoles.Add(new UserRole { Role = userRole });

            _db.Users.Add(user);
            await _db.SaveChangesAsync(cancellationToken);   // a duplicate that slipped past the check above hits the unique index: 409

            return ToProfile(user, new[] { Roles.User });
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(request.Email);
            var user = await LoadUser(u => u.NormalizedEmail == normalized, cancellationToken);

            if (user == null)
            {
                _passwords.VerifyDummy(request.Password);
                throw InvalidCredentials();
            }

            var now = DateTime.UtcNow;

            if (user.LockoutEnd is { } lockedUntil && lockedUntil > now)
                throw Locked(lockedUntil - now);

            var check = _passwords.Verify(user, request.Password);

            if (check == PasswordCheck.Failed)
            {
                await RegisterFailureAsync(user, now, cancellationToken);
                throw InvalidCredentials();
            }

            // Only someone who knows the password learns that the account is disabled.
            if (!user.IsActive)
                throw AccountDisabled();

            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            user.LastLoginAt = now;

            if (check == PasswordCheck.SuccessRehashNeeded)
                user.PasswordHash = _passwords.Hash(user, request.Password);   // upgrade an old, weaker hash

            var response = await IssueTokensAsync(user, familyId: Guid.NewGuid(), ipAddress, cancellationToken);
            return response;
        }

        // ---------- refresh / logout ----------

        public async Task<AuthResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw InvalidRefreshToken();

            var hash = TokenHasher.Hash(refreshToken.Trim());
            var stored = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

            if (stored == null)
                throw InvalidRefreshToken();

            var now = DateTime.UtcNow;

            // A token that was already used or revoked is being replayed: someone has a copy. End the whole session.
            if (stored.UsedAt != null || stored.RevokedAt != null)
            {
                _logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking its whole session.", stored.UserId);
                await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
                throw InvalidRefreshToken();
            }

            if (stored.ExpiresAt <= now)
                throw InvalidRefreshToken();

            var user = await LoadUser(u => u.id == stored.UserId, cancellationToken);

            if (user == null || !user.IsActive)
            {
                await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
                throw InvalidRefreshToken();
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Claim the token in one atomic step. If two requests present the same token at once, only one gets it.
            var claimed = await _db.RefreshTokens
                .Where(t => t.id == stored.id && t.UsedAt == null && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
                throw InvalidRefreshToken();
            }

            var response = await IssueTokensAsync(user, stored.FamilyId, ipAddress, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return response;
        }

        public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return;

            var hash = TokenHasher.Hash(refreshToken.Trim());
            var now = DateTime.UtcNow;

            // The whole session ends, so even a token that was rotated a moment ago cannot be used.
            var familyId = await _db.RefreshTokens.AsNoTracking()
                .Where(t => t.TokenHash == hash)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync(cancellationToken);

            if (familyId != null)
                await RevokeFamilyAsync(familyId.Value, now, cancellationToken);
        }

        public Task LogoutAllAsync(int userId, CancellationToken cancellationToken = default) =>
            RevokeAllAsync(userId, DateTime.UtcNow, cancellationToken);

        // ---------- passwords ----------

        public async Task<AuthResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
        {
            var user = await LoadUser(u => u.id == userId, cancellationToken) ?? throw new UnauthorizedException();
            var now = DateTime.UtcNow;

            if (user.LockoutEnd is { } lockedUntil && lockedUntil > now)
                throw Locked(lockedUntil - now);

            // Counts toward the lockout too: otherwise a stolen access token could be used to guess the password.
            if (_passwords.Verify(user, request.CurrentPassword) == PasswordCheck.Failed)
            {
                await RegisterFailureAsync(user, now, cancellationToken);
                throw new BusinessRuleException("The current password is incorrect.").WithKey("auth.wrong_current_password");
            }

            if (request.NewPassword == request.CurrentPassword)
                throw new BusinessRuleException("The new password must be different from the current one.").WithKey("auth.password_unchanged");

            _passwords.EnsureStrong(request.NewPassword, user.Email);

            // The new password, the sign-out of other devices and the new session happen together or not at all.
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            user.PasswordHash = _passwords.Hash(user, request.NewPassword);
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;

            await RevokeAllAsync(user.id, now, cancellationToken);   // every other device must sign in again

            // This device stays signed in with a fresh session (this also saves the user).
            var response = await IssueTokensAsync(user, Guid.NewGuid(), ipAddress, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return response;
        }

        public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(CleanEmail(email));
            var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.IsActive, cancellationToken);

            // The caller always gets the same answer, so this cannot be used to find out which addresses have accounts.
            if (user == null)
                return;

            var now = DateTime.UtcNow;

            // Only the newest link works.
            await _db.PasswordResetTokens
                .Where(t => t.UserId == user.id && t.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

            var token = TokenHasher.NewToken();

            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.id,
                TokenHash = TokenHasher.Hash(token),
                ExpiresAt = now.AddMinutes(_options.ResetTokenMinutes)
            });

            await _db.SaveChangesAsync(cancellationToken);

            _mailer.Queue(BuildResetEmail(user, token));
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(CleanEmail(request.Email));
            var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

            if (user == null || string.IsNullOrWhiteSpace(request.Token))
                throw InvalidResetToken();

            var hash = TokenHasher.Hash(request.Token.Trim());
            var now = DateTime.UtcNow;

            // A weak password is refused BEFORE the token is used, so a typo does not burn the link.
            _passwords.EnsureStrong(request.NewPassword, user.Email);

            // Using the token, changing the password and ending every session happen together or not at all.
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Use the token in one atomic step, so the same link cannot be used twice.
            var used = await _db.PasswordResetTokens
                .Where(t => t.UserId == user.id && t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

            if (used == 0)
                throw InvalidResetToken();

            user.PasswordHash = _passwords.Hash(user, request.NewPassword);
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;

            await _db.SaveChangesAsync(cancellationToken);
            await RevokeAllAsync(user.id, now, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        public async Task<UserProfile> GetProfileAsync(int userId, CancellationToken cancellationToken = default)
        {
            var user = await LoadUser(u => u.id == userId, cancellationToken);

            if (user == null || !user.IsActive)
                throw new UnauthorizedException();

            return ToProfile(user);
        }

        // ---------- helpers ----------

        private Task<User?> LoadUser(System.Linq.Expressions.Expression<Func<User, bool>> filter, CancellationToken cancellationToken) =>
            _db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(filter, cancellationToken);

        private async Task<AuthResponse> IssueTokensAsync(User user, Guid familyId, string? ipAddress, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var refresh = TokenHasher.NewToken();
            var refreshExpires = now.AddDays(_options.RefreshTokenDays);

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.id,
                TokenHash = TokenHasher.Hash(refresh),
                FamilyId = familyId,
                ExpiresAt = refreshExpires,
                CreatedByIp = ipAddress?.Length > 64 ? ipAddress[..64] : ipAddress
            });

            await _db.SaveChangesAsync(cancellationToken);

            var profile = ToProfile(user);
            var access = _tokens.CreateAccessToken(profile);

            return new AuthResponse
            {
                AccessToken = access.Token,
                AccessTokenExpiresAt = access.ExpiresAtUtc,
                RefreshToken = refresh,
                RefreshTokenExpiresAt = refreshExpires,
                User = profile
            };
        }

        private async Task RegisterFailureAsync(User user, DateTime now, CancellationToken cancellationToken)
        {
            var max = _options.Lockout.MaxFailedAttempts;

            if (max <= 0)
                return;

            user.FailedLoginCount++;

            if (user.FailedLoginCount >= max)
            {
                user.LockoutEnd = now.AddMinutes(_options.Lockout.Minutes);
                user.FailedLoginCount = 0;
                _logger.LogWarning("User {UserId} was locked until {LockoutEnd} after too many failed sign-ins.", user.id, user.LockoutEnd);
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another failed attempt was saved at the same moment; its count is what matters.
            }
        }

        private Task<int> RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken) =>
            _db.RefreshTokens
                .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

        private Task<int> RevokeAllAsync(int userId, DateTime now, CancellationToken cancellationToken) =>
            _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

        private EmailMessage BuildResetEmail(User user, string token)
        {
            var link = string.IsNullOrWhiteSpace(_options.ResetPasswordUrl)
                ? null
                : _options.ResetPasswordUrl
                    .Replace("{token}", Uri.EscapeDataString(token))
                    .Replace("{email}", Uri.EscapeDataString(user.Email));

            var values = new { name = user.Name, minutes = (int)_options.ResetTokenMinutes };

            var intro = _translator.Get("auth.reset_email_intro", values);
            var action = link != null
                ? _translator.Get("auth.reset_email_link", values)
                : _translator.Get("auth.reset_email_token", values);
            var ignore = _translator.Get("auth.reset_email_ignore", values);
            var subject = _translator.Get("auth.reset_email_subject", values);

            var html = $"<p>{WebUtility.HtmlEncode(intro)}</p>" +
                       (link != null
                           ? $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(action)}</a></p>"
                           : $"<p>{WebUtility.HtmlEncode(action)}</p><p><code>{WebUtility.HtmlEncode(token)}</code></p>") +
                       $"<p>{WebUtility.HtmlEncode(ignore)}</p>";

            var text = $"{intro}\n\n{(link != null ? link : action + "\n" + token)}\n\n{ignore}";

            var message = new EmailMessage { Subject = subject, HtmlBody = html, TextBody = text };
            message.AddTo(user.Email, user.Name);
            return message;
        }

        // ---------- shared small pieces ----------

        internal static string CleanEmail(string? email) => (email ?? string.Empty).Trim();

        internal static string Normalize(string email) => email.Trim().ToUpperInvariant();

        internal static UserProfile ToProfile(User user, IEnumerable<string>? roles = null) => new()
        {
            Id = user.id,
            Email = user.Email,
            Name = user.Name,
            IsActive = user.IsActive,
            Roles = (roles ?? user.UserRoles.Select(ur => ur.Role!.Name)).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            CreatedAt = user.CreateAt,
            LastLoginAt = user.LastLoginAt
        };

        private static UnauthorizedException InvalidCredentials() =>
            (UnauthorizedException)new UnauthorizedException("Invalid email or password.").WithKey("auth.invalid_credentials");

        private static UnauthorizedException AccountDisabled() =>
            (UnauthorizedException)new UnauthorizedException("This account is disabled.").WithKey("auth.account_disabled");

        private static UnauthorizedException InvalidRefreshToken() =>
            (UnauthorizedException)new UnauthorizedException("The refresh token is invalid or has expired.").WithKey("auth.invalid_refresh");

        private static BusinessRuleException InvalidResetToken() =>
            (BusinessRuleException)new BusinessRuleException("The reset link is invalid or has expired.").WithKey("auth.reset_invalid");

        private static TooManyRequestsException Locked(TimeSpan remaining) =>
            (TooManyRequestsException)new TooManyRequestsException("The account is temporarily locked after too many failed attempts.")
                .WithKey("auth.account_locked", new { minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) });
    }
}

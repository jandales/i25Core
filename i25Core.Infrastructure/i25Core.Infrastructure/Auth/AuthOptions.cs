namespace i25Core.Infrastructure.Auth
{
    /// <summary>Bound from the "Auth" section of appsettings.json.</summary>
    public class AuthOptions
    {
        public const string SectionName = "Auth";

        /// <summary>true: anyone can create an account at /api/Auth/register. false: only admins create accounts.</summary>
        public bool AllowRegistration { get; set; } = true;

        /// <summary>
        /// true (default): every endpoint needs a signed-in user unless it is marked [AllowAnonymous].
        /// false: endpoints are open unless they are marked [Authorize].
        /// </summary>
        public bool RequireAuthenticatedByDefault { get; set; } = true;

        /// <summary>How long a refresh token lasts. Each use issues a new one, so an active user stays signed in.</summary>
        public double RefreshTokenDays { get; set; } = 14;

        /// <summary>How long a password-reset link works.</summary>
        public double ResetTokenMinutes { get; set; } = 60;

        /// <summary>
        /// Address of your front-end's reset-password page, with {token} and {email} placeholders, e.g.
        /// "https://app.example.com/reset-password?token={token}&amp;email={email}". Empty: the email shows the token only.
        /// </summary>
        public string? ResetPasswordUrl { get; set; }

        public JwtOptions Jwt { get; set; } = new();
        public PasswordOptions Password { get; set; } = new();
        public LockoutOptions Lockout { get; set; } = new();
        public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();
    }

    public class JwtOptions
    {
        /// <summary>
        /// The secret that signs access tokens: at least 32 characters, random, and never committed to Git.
        /// Set it with an environment variable (Auth__Jwt__Key) or user secrets. In Development an empty key
        /// generates a temporary one (tokens stop working when the app restarts). Elsewhere an empty key stops the app.
        /// </summary>
        public string? Key { get; set; }

        public string Issuer { get; set; } = "i25Core";
        public string Audience { get; set; } = "i25Core";

        /// <summary>Lifetime of an access token. Short on purpose: a stolen token stops working soon.</summary>
        public double AccessTokenMinutes { get; set; } = 15;
    }

    public class PasswordOptions
    {
        public int MinLength { get; set; } = 10;

        /// <summary>Upper limit, so nobody can make the server hash a huge string.</summary>
        public int MaxLength { get; set; } = 128;

        /// <summary>PBKDF2 rounds. Higher is slower for attackers and for you. A hash made with fewer rounds is upgraded at the next sign-in.</summary>
        public int HashIterations { get; set; } = 210_000;
    }

    public class LockoutOptions
    {
        /// <summary>Wrong passwords in a row before the account is locked. 0 switches locking off.</summary>
        public int MaxFailedAttempts { get; set; } = 5;

        /// <summary>How long the account stays locked.</summary>
        public double Minutes { get; set; } = 15;
    }

    public class BootstrapAdminOptions
    {
        /// <summary>When both Email and Password are set and no Admin exists yet, this admin is created at startup.</summary>
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string Name { get; set; } = "Administrator";
    }
}

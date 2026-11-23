using System.Security.Cryptography;
using System.Text;
using i25Core.Core.Auth;
using i25Core.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace i25Core.Web.Auth
{
    /// <summary>
    /// The secret key that signs and checks access tokens. It comes from configuration (Auth:Jwt:Key).
    /// Only in Development, an empty key makes a temporary random one, so the project runs out of the box.
    /// </summary>
    public sealed class JwtSigningKey
    {
        public const int MinimumBytes = 32;

        public SymmetricSecurityKey Key { get; }

        public JwtSigningKey(IOptions<AuthOptions> options, IHostEnvironment environment, ILogger<JwtSigningKey> logger)
        {
            var configured = options.Value.Jwt.Key;
            byte[] bytes;

            if (string.IsNullOrWhiteSpace(configured))
            {
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        "Auth:Jwt:Key is not set. Set it to a random secret of at least 32 characters, for example with the " +
                        "environment variable Auth__Jwt__Key or user secrets. Never commit it to Git.");
                }

                bytes = RandomNumberGenerator.GetBytes(64);

                logger.LogWarning(
                    "Auth:Jwt:Key is empty, so a temporary signing key was generated. Everyone is signed out whenever the app restarts. " +
                    "Set Auth:Jwt:Key (user secrets) to keep sessions across restarts.");
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes(configured);

                if (bytes.Length < MinimumBytes)
                    throw new InvalidOperationException($"Auth:Jwt:Key must be at least {MinimumBytes} characters long.");
            }

            Key = new SymmetricSecurityKey(bytes);
        }
    }

    /// <summary>Creates signed JWT access tokens: sub (user id), email, name, role (one per role) and jti.</summary>
    public sealed class JwtTokenService : ITokenService
    {
        private readonly JwtOptions _options;
        private readonly JwtSigningKey _key;
        private readonly JsonWebTokenHandler _handler = new();

        public JwtTokenService(IOptions<AuthOptions> options, JwtSigningKey key)
        {
            _options = options.Value.Jwt;
            _key = key;
        }

        public AccessToken CreateAccessToken(UserProfile user)
        {
            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(_options.AccessTokenMinutes);

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                IssuedAt = now,
                NotBefore = now,
                Expires = expires,
                SigningCredentials = new SigningCredentials(_key.Key, SecurityAlgorithms.HmacSha256),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = user.Id.ToString(),
                    ["email"] = user.Email,
                    ["name"] = user.Name,
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    ["role"] = user.Roles.ToArray()
                }
            };

            return new AccessToken(_handler.CreateToken(descriptor), expires);
        }
    }
}

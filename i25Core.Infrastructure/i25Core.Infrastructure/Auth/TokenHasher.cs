using System.Security.Cryptography;
using System.Text;

namespace i25Core.Infrastructure.Auth
{
    /// <summary>Creates and hashes the random tokens used for refresh and password reset.</summary>
    internal static class TokenHasher
    {
        /// <summary>A new unguessable token: 32 random bytes (256 bits), URL-safe text of 43 characters.</summary>
        public static string NewToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>SHA-256 as 64 lower-case hex characters. Fine for high-entropy tokens (unlike passwords, they cannot be guessed).</summary>
        public static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}

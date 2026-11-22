using i25Core.Core.Entities;
using i25Core.Core.Exceptions;
using i25Core.Core.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Auth
{
    public enum PasswordCheck { Failed, Success, SuccessRehashNeeded }

    /// <summary>Hashes and checks passwords, and enforces the password rules.</summary>
    public class PasswordService
    {
        // Passwords that appear in every list of the most common ones. Compared ignoring case.
        private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
        {
            "password", "password1", "password12", "password123", "passw0rd", "p@ssw0rd", "p@ssword", "qwerty", "qwerty123",
            "qwertyuiop", "123456", "1234567", "12345678", "123456789", "1234567890", "12345678910", "0123456789", "111111",
            "000000", "abc123", "abcd1234", "iloveyou", "letmein", "welcome", "welcome1", "admin", "admin123", "administrator",
            "changeme", "default", "monkey", "dragon", "football", "baseball", "sunshine", "princess", "trustno1", "1q2w3e4r",
            "1qaz2wsx", "zaq12wsx", "qazwsxedc", "asdfghjkl", "zxcvbnm", "master", "login", "secret", "superman", "batman"
        };

        private readonly AuthOptions _options;
        private readonly ITranslator _translator;
        private readonly PasswordHasher<User> _hasher;
        private readonly string _dummyHash;

        public PasswordService(IOptions<AuthOptions> options, ITranslator translator)
        {
            _options = options.Value;
            _translator = translator;

            _hasher = new PasswordHasher<User>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
            {
                IterationCount = Math.Max(10_000, _options.Password.HashIterations)
            }));

            // Used to spend the same time on an email that has no account (see VerifyDummy).
            _dummyHash = _hasher.HashPassword(new User(), Guid.NewGuid().ToString("N"));
        }

        public string Hash(User user, string password) => _hasher.HashPassword(user, password);

        public PasswordCheck Verify(User user, string password)
        {
            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

            return result switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed
            };
        }

        /// <summary>
        /// Does the same amount of work as a real check, for an email that has no account. Without it, "no such
        /// user" would answer noticeably faster than "wrong password", and an attacker could use that to list accounts.
        /// </summary>
        public void VerifyDummy(string password) =>
            _hasher.VerifyHashedPassword(new User(), _dummyHash, password);

        /// <summary>Throws a 422 listing what is wrong with the password. Rules: length, not a very common password, does not contain the email name.</summary>
        public void EnsureStrong(string password, string email)
        {
            var problems = new List<string>();
            var rules = _options.Password;

            if (string.IsNullOrEmpty(password) || password.Length < rules.MinLength)
                problems.Add(_translator.Get("auth.password_too_short", new { min = rules.MinLength }));

            if (password != null && password.Length > rules.MaxLength)
                problems.Add(_translator.Get("auth.password_too_long", new { max = rules.MaxLength }));

            if (!string.IsNullOrEmpty(password))
            {
                if (Common.Contains(password.Trim()))
                    problems.Add(_translator.Get("auth.password_common"));

                var localPart = (email ?? string.Empty).Split('@')[0];

                if (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
                    problems.Add(_translator.Get("auth.password_contains_email"));

                if (password.Distinct().Count() <= 2)
                    problems.Add(_translator.Get("auth.password_repetitive"));
            }

            if (problems.Count > 0)
            {
                throw new BusinessRuleException(
                        "The password does not meet the requirements.",
                        new Dictionary<string, string[]> { ["password"] = problems.ToArray() })
                    .WithKey("auth.password_weak");
            }
        }
    }
}

namespace i25Core.Core.Auth
{
    /// <summary>Names of the built-in roles. Use them in <c>[Authorize(Roles = Roles.Admin)]</c>.</summary>
    public static class Roles
    {
        /// <summary>Can manage users and roles, and do everything a User can.</summary>
        public const string Admin = "Admin";

        /// <summary>The role every new account gets.</summary>
        public const string User = "User";

        public static readonly string[] BuiltIn = { Admin, User };
    }

    /// <summary>
    /// The person making the current request (Laravel's auth()->user()).
    /// Outside a request (a background job) nobody is signed in: <see cref="IsAuthenticated"/> is false.
    /// </summary>
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        /// <summary>The user's id, or null when nobody is signed in.</summary>
        int? Id { get; }

        string? Email { get; }
        string? Name { get; }
        IReadOnlyCollection<string> Roles { get; }
        bool IsInRole(string role);
    }

    /// <summary>A signed access token and when it stops being valid.</summary>
    public record AccessToken(string Token, DateTime ExpiresAtUtc);

    /// <summary>Creates the signed JWT access tokens.</summary>
    public interface ITokenService
    {
        AccessToken CreateAccessToken(UserProfile user);
    }

    // ---------- requests ----------

    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    // ---------- responses ----------

    public class UserProfile
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
        public DateTime? CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class AuthResponse
    {
        public string TokenType { get; set; } = "Bearer";

        /// <summary>Send it with every request: <c>Authorization: Bearer {accessToken}</c>. Short-lived.</summary>
        public string AccessToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; set; }

        /// <summary>Exchange it for a new pair of tokens at /api/Auth/refresh. Each one works once.</summary>
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime RefreshTokenExpiresAt { get; set; }

        public UserProfile User { get; set; } = new();
    }

    // ---------- administration ----------

    public class CreateUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string>? Roles { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class SetRolesRequest
    {
        public List<string> Roles { get; set; } = new();
    }

    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int UserCount { get; set; }
        public bool IsBuiltIn { get; set; }
    }

    public class CreateRoleRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    // ---------- services ----------

    /// <summary>Sign up, sign in, refresh tokens and password handling.</summary>
    public interface IAuthService
    {
        Task<UserProfile> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
        Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
        Task<AuthResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

        /// <summary>Ends one session by revoking its refresh token. Unknown or already-revoked tokens are ignored.</summary>
        Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>Ends every session of a user.</summary>
        Task LogoutAllAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes the password, signs the user out everywhere else, and returns a fresh session for this device.
        /// Wrong attempts count toward the account lockout.
        /// </summary>
        Task<AuthResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);

        /// <summary>Sends a reset link by email. Behaves the same whether or not the address has an account.</summary>
        Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

        Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
        Task<UserProfile> GetProfileAsync(int userId, CancellationToken cancellationToken = default);
    }

    /// <summary>Managing other people's accounts and the roles themselves (admin work).</summary>
    public interface IUserAdminService
    {
        Task<(List<UserProfile> Items, int Total)> ListUsersAsync(string? search, string? role, int page, int pageSize, CancellationToken cancellationToken = default);
        Task<UserProfile> GetUserAsync(int id, CancellationToken cancellationToken = default);
        Task<UserProfile> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
        Task<UserProfile> UpdateUserAsync(int id, UpdateUserRequest request, int actingUserId, CancellationToken cancellationToken = default);
        Task<UserProfile> SetRolesAsync(int id, SetRolesRequest request, int actingUserId, CancellationToken cancellationToken = default);
        Task UnlockUserAsync(int id, CancellationToken cancellationToken = default);
        Task DeleteUserAsync(int id, int actingUserId, CancellationToken cancellationToken = default);

        Task<List<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default);
        Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);
        Task DeleteRoleAsync(int id, CancellationToken cancellationToken = default);
    }
}

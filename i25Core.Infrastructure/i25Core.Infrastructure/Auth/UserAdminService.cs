using System.Text.RegularExpressions;
using i25Core.Core.Auth;
using i25Core.Core.Entities;
using i25Core.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace i25Core.Infrastructure.Auth
{
    public class UserAdminService : IUserAdminService
    {
        private static readonly Regex RoleName = new(@"^[A-Za-z][A-Za-z0-9_-]{1,63}$", RegexOptions.Compiled);

        private readonly I25DbContext _db;
        private readonly PasswordService _passwords;

        public UserAdminService(I25DbContext db, PasswordService passwords)
        {
            _db = db;
            _passwords = passwords;
        }

        // ---------- users ----------

        public async Task<(List<UserProfile> Items, int Total)> ListUsersAsync(
            string? search, string? role, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                query = query.Where(u => u.Email.Contains(text) || u.Name.Contains(text));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                var name = role.Trim();
                query = query.Where(u => u.UserRoles.Any(ur => ur.Role!.Name == name));
            }

            var total = await query.CountAsync(cancellationToken);

            var users = await query
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .OrderBy(u => u.id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (users.Select(u => AuthService.ToProfile(u)).ToList(), total);
        }

        public async Task<UserProfile> GetUserAsync(int id, CancellationToken cancellationToken = default) =>
            AuthService.ToProfile(await Find(id, tracking: false, cancellationToken));

        public async Task<UserProfile> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
        {
            var email = AuthService.CleanEmail(request.Email);
            _passwords.EnsureStrong(request.Password, email);

            if (await _db.Users.AnyAsync(u => u.NormalizedEmail == AuthService.Normalize(email), cancellationToken))
                throw new ConflictException("An account with this email already exists.").WithKey("auth.email_taken");

            var roles = await ResolveRolesAsync(request.Roles is { Count: > 0 } ? request.Roles : new List<string> { Roles.User }, cancellationToken);

            var user = new User
            {
                Email = email,
                NormalizedEmail = AuthService.Normalize(email),
                Name = request.Name.Trim(),
                IsActive = request.IsActive
            };

            user.PasswordHash = _passwords.Hash(user, request.Password);

            foreach (var role in roles)
                user.UserRoles.Add(new UserRole { Role = role });

            _db.Users.Add(user);
            await _db.SaveChangesAsync(cancellationToken);

            return AuthService.ToProfile(user, roles.Select(r => r.Name));
        }

        public async Task<UserProfile> UpdateUserAsync(int id, UpdateUserRequest request, int actingUserId, CancellationToken cancellationToken = default)
        {
            var user = await Find(id, tracking: true, cancellationToken);

            if (!request.IsActive && user.IsActive)
            {
                if (id == actingUserId)
                    throw new BusinessRuleException("You cannot deactivate your own account.").WithKey("auth.cannot_deactivate_self");

                await EnsureNotLastAdminAsync(user, cancellationToken);
            }

            user.Name = request.Name.Trim();
            user.IsActive = request.IsActive;

            await _db.SaveChangesAsync(cancellationToken);

            // A deactivated account is signed out everywhere (its access token still works until it expires).
            if (!user.IsActive)
                await RevokeAllAsync(user.id, cancellationToken);

            return AuthService.ToProfile(user);
        }

        public async Task<UserProfile> SetRolesAsync(int id, SetRolesRequest request, int actingUserId, CancellationToken cancellationToken = default)
        {
            var user = await Find(id, tracking: true, cancellationToken);
            var wanted = await ResolveRolesAsync(request.Roles, cancellationToken);

            var losesAdmin = user.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin) && wanted.All(r => r.Name != Roles.Admin);

            if (losesAdmin)
                await EnsureNotLastAdminAsync(user, cancellationToken);

            // Replace the set of roles.
            _db.UserRoles.RemoveRange(user.UserRoles.Where(ur => wanted.All(r => r.id != ur.RoleId)));

            foreach (var role in wanted.Where(r => user.UserRoles.All(ur => ur.RoleId != r.id)))
                _db.UserRoles.Add(new UserRole { UserId = user.id, RoleId = role.id });

            await _db.SaveChangesAsync(cancellationToken);

            return AuthService.ToProfile(user, wanted.Select(r => r.Name));
        }

        public async Task UnlockUserAsync(int id, CancellationToken cancellationToken = default)
        {
            var user = await Find(id, tracking: true, cancellationToken);
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteUserAsync(int id, int actingUserId, CancellationToken cancellationToken = default)
        {
            var user = await Find(id, tracking: true, cancellationToken);

            if (id == actingUserId)
                throw new BusinessRuleException("You cannot delete your own account.").WithKey("auth.cannot_delete_self");

            await EnsureNotLastAdminAsync(user, cancellationToken);

            _db.Users.Remove(user);           // role links and tokens are removed with it
            await _db.SaveChangesAsync(cancellationToken);
        }

        // ---------- roles ----------

        public async Task<List<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default)
        {
            var roles = await _db.Roles.AsNoTracking()
                .OrderBy(r => r.id)
                .Select(r => new RoleDto
                {
                    Id = r.id,
                    Name = r.Name,
                    Description = r.Description,
                    UserCount = r.UserRoles.Count
                })
                .ToListAsync(cancellationToken);

            foreach (var role in roles)
                role.IsBuiltIn = Roles.BuiltIn.Contains(role.Name, StringComparer.OrdinalIgnoreCase);

            return roles;
        }

        public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
        {
            var name = (request.Name ?? string.Empty).Trim();

            if (!RoleName.IsMatch(name))
                throw new BusinessRuleException("A role name needs 2-64 letters, digits, '_' or '-', starting with a letter.").WithKey("auth.role_name_invalid");

            if (await _db.Roles.AnyAsync(r => r.Name == name, cancellationToken))
                throw new ConflictException("A role with this name already exists.").WithKey("auth.role_exists");

            var role = new Role { Name = name, Description = request.Description?.Trim() };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync(cancellationToken);

            return new RoleDto { Id = role.id, Name = role.Name, Description = role.Description, UserCount = 0, IsBuiltIn = false };
        }

        public async Task DeleteRoleAsync(int id, CancellationToken cancellationToken = default)
        {
            var role = await _db.Roles.Include(r => r.UserRoles).FirstOrDefaultAsync(r => r.id == id, cancellationToken)
                ?? throw NotFoundException.ForEntity("Role");

            if (Roles.BuiltIn.Contains(role.Name, StringComparer.OrdinalIgnoreCase))
                throw new BusinessRuleException("Built-in roles cannot be deleted.").WithKey("auth.role_builtin");

            if (role.UserRoles.Count > 0)
                throw new BusinessRuleException("This role is still assigned to users.").WithKey("auth.role_in_use");

            _db.Roles.Remove(role);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // ---------- helpers ----------

        private async Task<User> Find(int id, bool tracking, CancellationToken cancellationToken)
        {
            var query = _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).AsQueryable();

            if (!tracking)
                query = query.AsNoTracking();

            return await query.FirstOrDefaultAsync(u => u.id == id, cancellationToken)
                ?? throw NotFoundException.ForEntity("User");
        }

        private async Task<List<Role>> ResolveRolesAsync(IEnumerable<string> names, CancellationToken cancellationToken)
        {
            var wanted = names.Select(n => (n ?? string.Empty).Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = await _db.Roles.Where(r => wanted.Contains(r.Name)).ToListAsync(cancellationToken);

            var missing = wanted.Where(w => found.All(f => !string.Equals(f.Name, w, StringComparison.OrdinalIgnoreCase))).ToList();

            if (missing.Count > 0)
            {
                throw new BusinessRuleException($"Unknown role: {string.Join(", ", missing)}.")
                    .WithKey("auth.role_not_found", new { roles = string.Join(", ", missing) });
            }

            return found;
        }

        /// <summary>Stops the last active administrator from being deleted, deactivated or demoted: nobody could manage accounts afterwards.</summary>
        private async Task EnsureNotLastAdminAsync(User user, CancellationToken cancellationToken)
        {
            var isAdmin = user.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin);

            if (!isAdmin || !user.IsActive)
                return;

            var otherActiveAdmins = await _db.Users.CountAsync(
                u => u.id != user.id && u.IsActive && u.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin), cancellationToken);

            if (otherActiveAdmins == 0)
                throw new BusinessRuleException("This is the last active administrator.").WithKey("auth.last_admin");
        }

        private Task<int> RevokeAllAsync(int userId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            return _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        }
    }
}

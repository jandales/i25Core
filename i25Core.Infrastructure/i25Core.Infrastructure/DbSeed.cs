using i25Core.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace i25Core.Infrastructure
{
    /// <summary>
    /// Reference/lookup data inserted through EF migrations (HasData).
    /// Ids are explicit because HasData requires stable keys.
    /// </summary>
    public static class DbSeed
    {
        private static readonly DateTime Stamp = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        public static void Apply(ModelBuilder mb)
        {
            // The two built-in roles. Ids are fixed so migrations stay stable. Accounts are never seeded:
            // there are no default passwords. The first admin is created from configuration (Auth:BootstrapAdmin).
            mb.Entity<Role>().HasData(
                new Role { id = 1, intConcurrencyId = 0, CreateAt = Stamp, ModifiedAt = Stamp, Name = "Admin", Description = "Manages users and roles; can do everything a User can." },
                new Role { id = 2, intConcurrencyId = 0, CreateAt = Stamp, ModifiedAt = Stamp, Name = "User", Description = "The default role for every account." });
        }
    }
}

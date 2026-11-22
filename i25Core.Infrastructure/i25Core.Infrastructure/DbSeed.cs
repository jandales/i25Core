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
            mb.Entity<Category>().HasData(
                new Category { id = 1, intConcurrencyId = 0, CreateAt = Stamp, ModifiedAt = Stamp, CategoryCode = "GEN", CategoryName = "General" },
                new Category { id = 2, intConcurrencyId = 0, CreateAt = Stamp, ModifiedAt = Stamp, CategoryCode = "RAW", CategoryName = "Raw Materials" },
                new Category { id = 3, intConcurrencyId = 0, CreateAt = Stamp, ModifiedAt = Stamp, CategoryCode = "FIN", CategoryName = "Finished Goods" });

        }
    }
}

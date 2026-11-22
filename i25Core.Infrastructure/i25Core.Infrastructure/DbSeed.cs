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

        }
    }
}

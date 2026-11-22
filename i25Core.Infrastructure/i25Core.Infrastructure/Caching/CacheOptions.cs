namespace i25Core.Infrastructure.Caching
{
    /// <summary>Bound from the "Cache" section of appsettings.json.</summary>
    public class CacheOptions
    {
        public const string SectionName = "Cache";

        /// <summary>"Memory" (default, single server) or "Redis" (shared across servers).</summary>
        public string Provider { get; set; } = "Memory";

        /// <summary>Redis connection string, e.g. "localhost:6379". Required when Provider is Redis.</summary>
        public string? RedisConnection { get; set; }

        /// <summary>Prefix added to every key so several apps can share one Redis.</summary>
        public string KeyPrefix { get; set; } = "i25core:";

        /// <summary>Used by ICacheService when no duration is passed.</summary>
        public int DefaultDurationSeconds { get; set; } = 300;

        public bool UseRedis => string.Equals(Provider, "Redis", StringComparison.OrdinalIgnoreCase);
    }
}

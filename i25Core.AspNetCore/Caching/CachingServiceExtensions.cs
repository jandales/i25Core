using i25Core.Core.Caching;
using i25Core.Infrastructure.Caching;

namespace i25Core.AspNetCore.Caching
{
    public static class CachingServiceExtensions
    {
        /// <summary>
        /// Registers ICacheService and response caching, backed by memory or Redis
        /// according to the "Cache" section of appsettings.json.
        /// </summary>
        public static IServiceCollection AddI25Caching(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(CacheOptions.SectionName);
            services.Configure<CacheOptions>(section);

            var options = section.Get<CacheOptions>() ?? new CacheOptions();

            if (options.UseRedis)
            {
                if (string.IsNullOrWhiteSpace(options.RedisConnection))
                    throw new InvalidOperationException("Cache:Provider is Redis but Cache:RedisConnection is empty.");

                services.AddStackExchangeRedisCache(redis =>
                {
                    redis.Configuration = options.RedisConnection;
                    redis.InstanceName = options.KeyPrefix;
                });

                services.AddStackExchangeRedisOutputCache(redis =>
                {
                    redis.Configuration = options.RedisConnection;
                    redis.InstanceName = options.KeyPrefix + "response:";
                });
            }
            else
            {
                services.AddDistributedMemoryCache();
            }

            services.AddOutputCache(output => output.AddBasePolicy(new ResponseCachePolicy()));

            services.AddSingleton<ICacheService, DistributedCacheService>();

            return services;
        }
    }
}

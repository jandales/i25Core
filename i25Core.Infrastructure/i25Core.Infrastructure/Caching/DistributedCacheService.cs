using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using i25Core.Core.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Caching
{
    /// <summary>
    /// ICacheService on top of IDistributedCache. The same code runs on the in-memory
    /// distributed cache or Redis; only the registration changes.
    /// </summary>
    public class DistributedCacheService : ICacheService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            // Entities often have navigation properties pointing back at each other.
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        // One lock per key so concurrent misses run the factory once (per server).
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

        private readonly IDistributedCache _cache;
        private readonly CacheOptions _options;

        public DistributedCacheService(IDistributedCache cache, IOptions<CacheOptions> options)
        {
            _cache = cache;
            _options = options.Value;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var bytes = await _cache.GetAsync(FullKey(key), cancellationToken);

            return bytes == null
                ? default
                : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

            return _cache.SetAsync(FullKey(key), bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration ?? TimeSpan.FromSeconds(_options.DefaultDurationSeconds)
            }, cancellationToken);
        }

        public async Task<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            var fullKey = FullKey(key);

            var cached = await _cache.GetAsync(fullKey, cancellationToken);
            if (cached != null)
                return JsonSerializer.Deserialize<T>(cached, JsonOptions)!;

            var gate = Locks.GetOrAdd(fullKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);

            try
            {
                // Another request may have filled it while we waited.
                cached = await _cache.GetAsync(fullKey, cancellationToken);
                if (cached != null)
                    return JsonSerializer.Deserialize<T>(cached, JsonOptions)!;

                var value = await factory(cancellationToken);
                await SetAsync(key, value, duration, cancellationToken);
                return value;
            }
            finally
            {
                gate.Release();
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            return _cache.RemoveAsync(FullKey(key), cancellationToken);
        }

        private string FullKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Cache key cannot be empty.", nameof(key));

            return _options.KeyPrefix + key;
        }
    }
}

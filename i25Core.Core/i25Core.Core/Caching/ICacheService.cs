namespace i25Core.Core.Caching
{
    /// <summary>
    /// General-purpose key/value cache (Laravel's Cache:: equivalent).
    /// Backed by memory or Redis depending on the "Cache:Provider" setting.
    /// Values are stored as JSON, so callers always get their own copy.
    /// </summary>
    public interface ICacheService
    {
        /// <summary>Returns the cached value, or default when the key is missing. (Cache::get)</summary>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

        /// <summary>Stores a value. Uses Cache:DefaultDurationSeconds when no duration is given. (Cache::put)</summary>
        Task SetAsync<T>(string key, T value, TimeSpan? duration = null, CancellationToken cancellationToken = default);

        /// <summary>Returns the cached value, or runs the factory, caches and returns its result. (Cache::remember)</summary>
        Task<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a key. (Cache::forget)</summary>
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    }
}

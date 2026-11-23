namespace i25Core.AspNetCore.Caching
{
    /// <summary>
    /// Opt a controller (or a single action) into response caching.
    /// GET responses are cached per URL + query string; any successful
    /// POST/PUT/DELETE on the same controller clears that controller's cache.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public sealed class CacheResponseAttribute : Attribute
    {
        /// <summary>How long a response stays cached. Default 60 seconds.</summary>
        public int DurationSeconds { get; set; } = 60;
    }
}

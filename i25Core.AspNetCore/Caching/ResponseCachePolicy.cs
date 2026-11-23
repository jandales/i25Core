using Microsoft.AspNetCore.OutputCaching;

namespace i25Core.AspNetCore.Caching
{
    /// <summary>
    /// Output-cache policy applied to every endpoint, but it only caches GET/HEAD
    /// requests on endpoints marked [CacheResponse]. Each cached response is tagged
    /// with its controller name so writes can evict exactly that controller's entries.
    /// </summary>
    public sealed class ResponseCachePolicy : IOutputCachePolicy
    {
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            var http = context.HttpContext;
            var attribute = http.GetEndpoint()?.Metadata.GetMetadata<CacheResponseAttribute>();
            var controller = http.GetRouteValue("controller")?.ToString();

            var cacheable =
                attribute != null &&
                controller != null &&
                (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) &&
                // Never share responses meant for one signed-in user.
                !http.Request.Headers.ContainsKey("Authorization");

            context.EnableOutputCaching = cacheable;
            context.AllowCacheLookup = cacheable;
            context.AllowCacheStorage = cacheable;
            context.AllowLocking = true; // concurrent misses wait for one response instead of all hitting the DB

            if (!cacheable)
                return ValueTask.CompletedTask;

            context.ResponseExpirationTimeSpan = TimeSpan.FromSeconds(attribute!.DurationSeconds);

            // /get?Filters=...&Page=2 and /get?Page=3 are different cache entries.
            context.CacheVaryByRules.QueryKeys = "*";

            context.Tags.Add(ResponseCacheTags.ForController(controller!));

            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            // Only keep successful responses; a 404 or 400 should not stick around.
            if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
                context.AllowCacheStorage = false;

            return ValueTask.CompletedTask;
        }
    }

    public static class ResponseCacheTags
    {
        /// <summary>Tag shared by every cached response of one controller, e.g. "controller:Vendor".</summary>
        public static string ForController(string controllerName) =>
            "controller:" + controllerName.ToLowerInvariant();
    }
}

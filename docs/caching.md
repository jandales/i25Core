# Caching

i25Core has two caching layers. Both are backed by **in-memory** storage (the default) or **Redis**, and one setting switches between them.

| Layer | Use it for | Laravel equivalent |
|---|---|---|
| **`ICacheService`** | Caching any value in your own code: lookups, computed results, external API calls | `Cache::get / put / remember / forget` |
| **`[CacheResponse]`** | Caching whole GET responses of a controller, cleared automatically on writes | Response cache middleware |

---

## Configuration

`appsettings.json`:

```json
"Cache": {
  "Provider": "Memory",
  "RedisConnection": "",
  "KeyPrefix": "i25core:",
  "DefaultDurationSeconds": 300
}
```

| Setting | Default | Description |
|---|---|---|
| `Provider` | `Memory` | `Memory` keeps the cache inside each server process. `Redis` shares one cache across all servers. |
| `RedisConnection` | — | Required for Redis, e.g. `localhost:6379` or `myredis.redis.cache.windows.net:6380,password=…,ssl=True` |
| `KeyPrefix` | `i25core:` | Added to every key, so several apps can share one Redis |
| `DefaultDurationSeconds` | `300` | Expiry used by `ICacheService` when no duration is passed |

**Switching to Redis** is a configuration change only:

```json
"Cache": { "Provider": "Redis", "RedisConnection": "localhost:6379" }
```

Use `Memory` for a single server or local development. Use `Redis` when the API runs on more than one server, because each server would otherwise keep its own cache and clearing one wouldn't clear the others.

---

## 1. `ICacheService`: cache anything

Inject `ICacheService` (from `i25Core.Core.Caching`) into a business layer, controller or service.

| Method | Laravel | What it does |
|---|---|---|
| `GetAsync<T>(key)` | `Cache::get` | Returns the value, or `default` if missing |
| `SetAsync(key, value, duration?)` | `Cache::put` | Stores a value |
| `GetOrSetAsync(key, factory, duration?)` | `Cache::remember` | Returns the cached value, or runs `factory`, stores the result and returns it |
| `RemoveAsync(key)` | `Cache::forget` | Deletes a key |

### Example: cache a lookup list

```csharp
public class CurrencyBusinessLayer : BaseBusinessLayer<Currency>
{
    private readonly ICacheService _cache;

    public CurrencyBusinessLayer(IRepository db, ICacheService cache) : base(db)
    {
        _cache = cache;
    }

    public Task<List<Currency>> GetActiveAsync() =>
        _cache.GetOrSetAsync(
            "currencies:active",
            ct => _dbSet.Where(c => c.Active).ToListAsync(ct),
            TimeSpan.FromHours(1));

    public override async Task UpdateSync(Currency entity)
    {
        await base.UpdateSync(entity);
        await _cache.RemoveAsync("currencies:active");   // keep it fresh
    }
}
```

### Behaviour

- **Values are stored as JSON.** Every caller gets its own copy, so changing a cached object can't affect other requests. Navigation-property cycles are ignored.
- **`GetOrSetAsync` runs the factory only once per key per server,** even when many requests miss at the same moment. The others wait and get the same result.
- **Keys are prefixed with `KeyPrefix`.** You pass `"currencies:active"`, and Redis stores `i25core:currencies:active`.
- **Keys are case-sensitive.** An empty key throws `ArgumentException`.

---

## 2. `[CacheResponse]`: cache a controller's GET responses

Add the attribute to a controller:

```csharp
using i25Core.Web.Caching;

[CacheResponse(DurationSeconds = 300)]
public class CurrencyController : BaseController<Currency>
{
    public CurrencyController(IBusinessLayer<Currency> service) : base(service) { }
}
```

That's all. The controller now behaves like this:

| Request | Behaviour |
|---|---|
| `GET /api/Currency`, `/api/Currency/5`, `/api/Currency/get?...` | First call hits the database. Repeat calls within `DurationSeconds` are served from the cache with no database query. |
| Different query strings (`?Filters=A` vs `?Filters=B`, `?Page=2`) | Cached separately |
| `POST` / `PUT` / `DELETE` on `/api/Currency` that succeeds | **All** cached responses of `CurrencyController` are cleared, so the next GET returns fresh data |
| Non-200 responses (404, 400, 500) | Never cached |
| Requests with an `Authorization` header | Never cached, so one user's data isn't served to another |
| Controllers **without** `[CacheResponse]` | Never cached |

The attribute can also go on a single action:

```csharp
[HttpGet("summary")]
[CacheResponse(DurationSeconds = 30)]
public async Task<IActionResult> Summary() { ... }
```

### Clearing the cache from custom actions

`Create`, `Update` and `Delete` in `BaseController` already clear the cache. A custom write action should do the same:

```csharp
[HttpPost("{id}/post")]
public async Task<IActionResult> Post(int id)
{
    await _service.Post(id);
    await EvictResponseCacheAsync();      // clears this controller's cached GETs
    return OkResponse<object?>(null, "Posted.");
}
```

To clear **another** controller's cache, for example when saving a vendor changes the vendor list shown elsewhere:

```csharp
var store = HttpContext.RequestServices.GetRequiredService<IOutputCacheStore>();
await store.EvictByTagAsync(ResponseCacheTags.ForController("Vendor"), HttpContext.RequestAborted);
```

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/Caching/ICacheService.cs` | **New.** Cache interface, with no dependency on any cache technology |
| `i25Core.Infrastructure/Caching/CacheOptions.cs` | **New.** `Cache` settings |
| `i25Core.Infrastructure/Caching/DistributedCacheService.cs` | **New.** `ICacheService` over `IDistributedCache`, with JSON serialization and per-key locking |
| `i25Core.AspNetCore/Caching/CacheResponseAttribute.cs` | **New.** Opt-in attribute |
| `i25Core.AspNetCore/Caching/ResponseCachePolicy.cs` | **New.** Output-cache policy: decides what to cache, how long, the vary-by-query rule and the tag |
| `i25Core.AspNetCore/Caching/CachingServiceExtensions.cs` | **New.** `AddI25Caching()` registers memory or Redis from config |
| `i25Core.AspNetCore/Controllers/BaseController.cs` | Clears the controller's tag after `Create` / `Update` / `Delete`; adds `EvictResponseCacheAsync()` |
| `i25Core.Web/Program.cs` | `AddI25Caching(...)` and `app.UseOutputCache()` |
| `i25Core.Web/appsettings.json` | `Cache` section |
| `i25Core.Web.csproj` | `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis` (8.0.11) |

### Building blocks

| Piece | Built on |
|---|---|
| `ICacheService` | `IDistributedCache`: `AddDistributedMemoryCache()` or `AddStackExchangeRedisCache()` |
| `[CacheResponse]` | ASP.NET Core **Output Caching** (`AddOutputCache` / `UseOutputCache`): in-memory store, or `AddStackExchangeRedisOutputCache()` |

Both use built-in ASP.NET Core components. i25Core adds only the configuration switch, the opt-in policy and the automatic clearing.

### Response caching flow

```
GET /api/Currency/get?Filters=...
   │
   ▼
UseOutputCache ──► ResponseCachePolicy.CacheRequestAsync
                      • endpoint has [CacheResponse]?   no → skip caching
                      • GET/HEAD and no Authorization?   no → skip caching
                      • expiry  = DurationSeconds
                      • vary by = all query-string keys
                      • tag     = "controller:currency"
   │
   ├─ cached?  yes → return stored response (no controller, no DB)
   │
   └─ no → controller → DB → 200 response
              │
              ▼
          ServeResponseAsync: status 200? → store it under the tag

POST/PUT/DELETE /api/Currency
   └─ BaseController → _service.Add/Update/Delete
                     → EvictResponseCacheAsync()
                         → IOutputCacheStore.EvictByTagAsync("controller:currency")
```

### Design decisions

| Decision | Reason |
|---|---|
| **Response caching is opt-in** (`[CacheResponse]`) | Caching everything by default risks serving stale data, for example after changes made through stored procedures or other controllers. You choose which data is safe to cache. |
| **Clear the whole controller on any write** | Simple and always correct. A single change can affect every list, filter and page of that entity, and tracking which cached pages contain the record would be complex and fragile. |
| **Tags per controller** | One tag clears every URL variant (`/`, `/5`, `/get?…`) in one call, in memory or Redis |
| **Vary by all query keys** | Paging, filters and sorts produce different results, so each combination gets its own entry |
| **Don't cache with `Authorization` or non-200** | Prevents leaking one user's data, and stops errors from "sticking" |
| **Locking on cache misses** | Output caching (`AllowLocking`) and `GetOrSetAsync` (a per-key semaphore) both prevent a stampede, where many simultaneous misses all hit the database |
| **JSON storage in `ICacheService`** | Works the same in memory and in Redis, and hands every caller an independent copy |
| **`ICacheService` interface in Core** | Business layers depend on the abstraction only, so the technology can change without touching them |
| **Implemented in Web and Infrastructure, not `BaseBusinessLayer`** | Keeps caching separate from data access, and avoids conflicts with the paging and filter features |

---

## Limitations

| Limitation | Notes |
|---|---|
| Writes outside `BaseController` don't clear response caches automatically | Stored procedures, other controllers, background jobs. Call `EvictResponseCacheAsync()` or `EvictByTagAsync(...)` yourself. |
| `Memory` provider is per server | With several servers, use `Redis` so clearing reaches all of them |
| `GetOrSetAsync` locking is per server | With Redis and several servers, each server may run the factory once on a cold key |
| Redis wiring wasn't exercised in testing | It uses the standard Microsoft Redis packages and needs a running Redis to verify |
| Requests with `Authorization` are never response-cached | Once authentication is added, per-user caching would need its own policy |

---

## Testing performed

These were run against the API with the `Memory` provider, a temporary entity, a controller with `[CacheResponse(DurationSeconds = 3)]`, and a temporary controller using `ICacheService`. Database hits were counted from EF's SQL command log. The temporary code and database were removed afterwards.

| # | Case | Expected | Result |
|---|---|---|---|
| 1 | GET list twice | 1 DB query, identical responses | ✓ |
| 2 | 3 GETs with 2 different filters | 2 DB queries (cached per query string) | ✓ |
| 3 | POST, then GET | Cache cleared: 1 DB query, new row visible | ✓ |
| 4 | GET missing id (404) twice | 2 DB queries (errors not cached) | ✓ |
| 5 | GET after 4 s (duration 3 s) | Expired: 1 DB query | ✓ |
| 6 | Controller without `[CacheResponse]`, GET twice | 2 DB queries | ✓ |
| 7 | GET with `Authorization` header twice | 2 DB queries | ✓ |
| 8 | PUT, then GET by id | Edited value returned | ✓ |
| 9 | `GetOrSetAsync` twice, `RemoveAsync`, then again | Factory ran once, then once more after remove | ✓ |
| 10 | 5 concurrent `GetOrSetAsync` misses on a new key | Factory ran **once**, all 5 got the same value | ✓ |

# Rate limiting

Caps how many requests one caller can make in a time window. Over the limit the API answers **429 Too Many Requests**
with a `Retry-After` header. It stops password guessing, scraping and one caller from using up the server.

Built on ASP.NET Core's rate limiter (no extra package). Code: `i25Core.AspNetCore/RateLimiting/`.

## What is limited

| Limit | Who is counted | Default |
|---|---|---|
| **Global, anonymous** | each client address | 60 requests / 60 s |
| **Global, authenticated** | each user (by id) | 300 requests / 60 s |
| **`auth` policy** | each client address (always, even if signed in) | 10 requests / 60 s |
| **`strict` policy** | user if signed in, else address | 5 requests / 60 s |

- The **global** limit applies to every request that reaches it. The anonymous and authenticated buckets are separate.
- The **`auth` policy** is on register, login, refresh, change-password, forgot-password and reset-password. All six
  share one counter per address, so 10 login tries and 10 register calls together use the same budget. `me`, `logout`
  and `logout-all` only use the global limit.
- A request on an `auth` endpoint counts against both the `auth` and the global limit.
- `/health` is never limited (probes); change it with `ExemptPaths`.
- Account lockout (5 wrong passwords per account) still exists. The two protect different things: lockout is per
  account, rate limiting is per client address.

## Using it on your own endpoints

```csharp
[EnableRateLimiting("strict")]        // 5 per minute
[HttpPost("export")]
public IActionResult Export() { ... }
```

Define your own name in `appsettings.json` (`"Policies": { "reports": { "PermitLimit": 20, "WindowSeconds": 60 } }`)
and use `[EnableRateLimiting("reports")]`. `[DisableRateLimiting]` switches off **all** limiting for an endpoint,
the global limit too, so use it sparingly.

## Configuration

```json
"RateLimiting": {
  "Enabled": true,
  "Global": {
    "Anonymous":     { "PermitLimit": 60,  "WindowSeconds": 60 },
    "Authenticated": { "PermitLimit": 300, "WindowSeconds": 60 }
  },
  "Policies": {
    "auth":   { "PermitLimit": 10, "WindowSeconds": 60 },
    "strict": { "PermitLimit": 5,  "WindowSeconds": 60 }
  },
  "ExemptPaths": [ "/health" ],
  "SegmentsPerWindow": 6
}
```

The app refuses to start when a limit or window is below 1. Any value can be set by environment variable, for
example `RateLimiting__Policies__auth__PermitLimit=5`.

## The response

```
HTTP/1.1 429 Too Many Requests
Retry-After: 12
{"success":false,"message":"Too many requests. Try again in 12 seconds.","traceId":"...","data":null,...}
```

The message is translated (`errors.rate_limited` in `lang/*/errors.json`, English and Spanish). CORS and security
headers are on it, so a browser front end can read it. The request log line for a refused request has
`RateLimitPolicy` (`global`, `auth`, ...).

## Behind a proxy or load balancer: ForwardedHeaders

Behind a reverse proxy every request seems to come from the proxy, so everyone would share one bucket. Turn on
`ForwardedHeaders` and **list your proxies**:

```json
"ForwardedHeaders": { "Enabled": true, "KnownProxies": [ "10.0.0.5" ], "KnownNetworks": [ "10.1.0.0/16" ], "ForwardLimit": 1 }
```

- It only believes `X-Forwarded-For` / `X-Forwarded-Proto` from a listed proxy. From anyone else the header is
  ignored, so a caller cannot pick its own address and escape the limit.
- Enabling it with no proxy listed stops the app at startup (it would be a hole).
- It also fixes the client address in the logs and HTTPS detection (HSTS, redirect) behind a TLS-terminating proxy.
- Leave it **off** when the app faces the internet directly.

## How it works

- Sliding window: the window is cut into `SegmentsPerWindow` parts, so the allowance returns smoothly instead of
  all at once at a window boundary. No queueing: a request over the limit is refused at once.
- The middleware sits **after authentication** (so signed-in callers are counted per user) and before authorization.
  Static files, the Hangfire dashboard and CORS preflights are answered before it and are not counted.
- **IPv6**: a client gets a whole `/64` network as one bucket, so rotating addresses inside its own range does not
  help it.
- Counters live in memory in each app instance and idle ones are dropped by the framework.

## Decisions and limitations

- **Per instance.** With several instances behind a load balancer each keeps its own counters, so the real limit
  is up to `instances x limit`. A shared counter (Redis) is not included.
- **Per address, not per target account.** Many addresses together can still attack one account slowly; account
  lockout covers that for sign-in. Flooding a victim's mailbox through forgot-password from many addresses is not covered.
- Counters reset when the app restarts.
- No `RateLimit-Remaining` headers; clients only see `Retry-After` once refused.
- Addresses behind one NAT (an office, a mobile carrier) share a bucket. Raise the anonymous limit if that hurts.
- If a proxy is misconfigured and the app sees only the proxy's address, everyone shares one bucket and gets refused
  quickly. That is visible and safe, and fixed with `ForwardedHeaders`.
- A custom or `strict` policy uses the same code as `auth`; only `auth` and the global limits were exercised in tests.

## Testing performed

Against a scratch database, with low limits set by environment variables, restarting the app per scenario:

| Case | Result |
|---|---|
| Anonymous global limit: first N pass, the rest 429; window passing lets calls through again | passed |
| `Retry-After` is a number inside the window; envelope has `success=false`, message, `traceId`; Spanish with `Accept-Language: es` | passed |
| `/health/live` and `/health/ready` never limited while the API is | passed |
| `auth` policy: 4 wrong logins reach the check, then 429; register, forgot, refresh, reset share the counter | passed |
| `me` and `logout` are not under the `auth` limit | passed |
| Signed in: counted per user (one admin blocked, another user and the anonymous bucket unaffected) | passed |
| 40 parallel requests against a limit of 15: exactly 15 admitted | passed |
| CORS and security headers on a 429; a preflight is answered and not counted | passed |
| Forwarded headers off: a fake `X-Forwarded-For` gets no new bucket | passed |
| Trusted proxy (address and CIDR): each forwarded client its own bucket; logged `ClientIp` is the forwarded one | passed |
| Untrusted proxy: header ignored | passed |
| IPv6: same `/64` shares a bucket, another `/64` does not | passed |
| `Enabled=false` limits nothing | passed |
| Startup refuses: limit 0, window 0, no proxy listed, a proxy that is not an IP, a network that is not CIDR, segments 0 | passed |
| Request log line of a refused request shows `RateLimitPolicy` | passed |

Defect found while testing: with `[DisableRateLimiting]` on `me` and the log-outs (my first design) those endpoints
escaped the **global** limit as well. The `auth` policy is now put only on the six credential endpoints instead.

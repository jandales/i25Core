# CORS and security headers

Two small protections for an API that browsers call.

- **CORS** decides which front-end sites may call the API from a browser.
- **Security headers** tell the browser not to guess, frame or sniff what the API returns.

Everything is configured in `appsettings.json`; the code is in `i25Core.AspNetCore/Security/`.

## CORS

Off by default. With `"AllowedOrigins": []` the API sends no CORS headers, so a page on another site cannot read its
responses. Turn it on by listing your front end:

```json
"Cors": {
  "AllowedOrigins": [ "https://app.example.com", "http://localhost:3000" ],
  "AllowCredentials": false
}
```

| Setting | Default | Meaning |
|---|---|---|
| `AllowedOrigins` | `[]` | Exact origins: scheme + host + optional port, no path. `"*"` = any origin. |
| `AllowedMethods` | GET POST PUT PATCH DELETE | Methods a browser may use. |
| `AllowedHeaders` | Authorization, Content-Type, Accept, Accept-Language, X-Locale, X-Api-Version | Request headers a browser may send. Add your own if you add custom headers. |
| `ExposedHeaders` | WWW-Authenticate, Content-Language, Retry-After | Response headers the front end's JavaScript may read. |
| `AllowCredentials` | `false` | Lets the browser send cookies. Needs explicit origins. |
| `MaxAgeSeconds` | `600` | How long the browser may remember a preflight answer. |

Notes:
- The app **refuses to start** with a clear message for: `"*"` together with `AllowCredentials`, `"*"` mixed with
  other origins, an origin with a path, an origin without a scheme, or the literal `null`.
- A trailing slash is tolerated (`https://app.example.com/` is read as `https://app.example.com`).
- The list is left out of a config file = built-in default. (.NET configuration adds to arrays that already have
  items, so the defaults are applied in code, not as initial array values.)
- The bearer-token design does not need cookies, so `AllowCredentials` stays `false` unless you add cookie auth.

## Security headers

Sent on **every** response, including errors and 401s, because the middleware is first in the pipeline.

| Header | Default | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | The browser must trust the declared content type. |
| `X-Frame-Options` | `DENY` | The API cannot be shown in a frame (clickjacking). |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | A JSON API never needs to load anything. |
| `Referrer-Policy` | `no-referrer` | No URL leaks to other sites. |
| `Permissions-Policy` | camera, microphone, geolocation, payment off | Browser features the API never uses. |
| `Cross-Origin-Opener-Policy` | `same-origin` | Isolates the browsing context. |
| `Cross-Origin-Resource-Policy` | `same-site` | Other sites cannot embed API responses or public files. `same-site` still allows a front end on a sibling subdomain. |
| `Strict-Transport-Security` | 365 days | Browsers keep using HTTPS. Sent only over HTTPS and never in Development. |
| `Cache-Control: no-store` + `Pragma: no-cache` | on `/api/Auth/...` | Sign-in and token responses are not stored by browsers or proxies. |
| `Server` | removed | Does not announce "Kestrel". |

Each value can be changed in the `SecurityHeaders` section; an **empty string removes that header**, and
`"Enabled": false` turns the whole feature off (HSTS included).

## How it works

- `UseI25SecurityHeaders()` is the first middleware. It registers an `OnStarting` callback, so the headers are
  added when the response starts, and **only when the header is not already set**. That is why the public file
  storage keeps its own stricter `Content-Security-Policy: sandbox`.
- `UseI25Cors()` runs right after the exception handler and **before** the HTTPS redirect and authentication.
  A browser preflight (`OPTIONS`) carries no token and cannot follow a redirect, so it must be answered first.
  Real calls that are refused (401) still carry the CORS headers, so the front end can read the status.
- Kestrel's `Server` header is removed in `ConfigureI25Security()` (before `Build()`).
- The Content-Security-Policy is skipped for `/swagger` and `/jobs` (listed in `ContentSecurityPolicySkipPaths`)
  because Swagger UI and the Hangfire dashboard need scripts. Swagger only runs in Development; `/jobs` is local-only.

## Decisions and limitations

- **CORS is not a security boundary for the API.** It only controls what browsers let a page read. `curl` or another
  server ignores it. Authentication still protects the data.
- HSTS is skipped for `localhost`, `127.0.0.1` and `[::1]` by ASP.NET Core itself. If your host sends HSTS from a
  reverse proxy, set `Hsts.Enabled` to `false` to avoid double headers. Do not enable `Preload` before you are sure
  every subdomain works over HTTPS (browsers cannot easily undo it).
- Behind a proxy that ends HTTPS, make sure forwarded headers are configured, otherwise the app sees plain HTTP
  and sends no HSTS (not part of this feature).
- No per-endpoint CORS policy: one policy for the whole API.
- Unknown URLs answer 401 when you are not signed in (the secure-by-default policy from the authentication
  feature), not 404.

## Testing performed

Against a scratch database, with curl from different `Origin` values (the app was restarted with each setting):

| Case | Result |
|---|---|
| Security headers on 200, 401, unknown URL, 400 | passed |
| No `Server` header; `RemoveServerHeader=false` brings it back | passed |
| `no-store` on `/api/Auth/*` and `/api/v1/Auth/*`; normal endpoints not forced | passed |
| No CSP on `/swagger`; the file storage's own `sandbox` CSP is kept | passed |
| CORS off by default: no headers, preflight not answered | passed |
| Allowed origin echoed with `Vary: Origin`; exposed headers; look-alike, other-scheme and other origins refused | passed |
| Preflight to a protected endpoint without token: 204 with methods, headers, max-age | passed |
| Disallowed header and method not granted; lists have no duplicates | passed |
| A 401 response carries the CORS header; versioned URL works too | passed |
| Credentials with an explicit origin; `*` answers `*` | passed |
| Startup refuses: `*` + credentials, path in origin, no scheme, mixing `*`, `null` | passed |
| `Enabled=false`, tuned values, empty value drops a header | passed |
| HSTS over HTTPS (real certificate, non-localhost name): Production yes, tuned, disabled, Development none | passed |

Defect found while testing: the allowed methods and headers appeared twice in the preflight answer because
.NET configuration appends to default arrays. The defaults are now applied in code.

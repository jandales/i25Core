# Health checks

Endpoints that tell a load balancer, Kubernetes, uptime monitor or a person whether the API is alive and able to work.

| URL | Who | Answers | Checks |
|---|---|---|---|
| `GET /health/live` | anyone | `{"status":"Healthy"}` | Nothing. The process answers. |
| `GET /health/ready` (also `GET /health`) | anyone | `{"status":"Healthy"}` | Database, storage, background jobs |
| `GET /health/details` | **Admin only** | every check with description, error text, timing | same as ready, never cached |

Status codes: **200** for Healthy and Degraded, **503** for Unhealthy.

## What each check does

- **database**: runs `SELECT 1`. Also counts migrations that are not applied: reachable but behind = **Degraded**.
- **storage**: reads (lists) every configured disk, and on the default disk writes a small probe file, reads it back
  and deletes it. Any failure = **Unhealthy**. The probe is never written to the public disk, so nothing
  temporary can be downloaded.
- **background-jobs**: opens the Hangfire job store and looks for a job server with a recent heartbeat.
  Store unreachable = **Unhealthy**. No server running = **Degraded** (the API still works; jobs wait).

## Why live and ready are separate

- **live** = "restart me if this fails". It checks nothing else, so a database outage never makes an orchestrator
  kill and restart healthy app instances (which would make the outage worse).
- **ready** = "send me traffic only if this passes". A database outage turns it 503 and the instance leaves the
  load balancer until the database is back.

Kubernetes example:

```yaml
livenessProbe:  { httpGet: { path: /health/live,  port: 8080 } }
readinessProbe: { httpGet: { path: /health/ready, port: 8080 } }
```

## Configuration

```json
"HealthChecks": {
  "Enabled": true,
  "Path": "/health",
  "TimeoutSeconds": 5,
  "CacheSeconds": 5,
  "Database": true,
  "Storage": true,
  "BackgroundJobs": true
}
```

- `Path` must start with `/` and not end with `/`; the app refuses to start otherwise.
- `TimeoutSeconds`: a check that runs longer counts as failed (Unhealthy).
- `CacheSeconds`: the anonymous `/ready` answer is reused for this long and only one check run happens at a time,
  so a probe called in a loop cannot become load on the database. `0` turns it off. `/details` is never cached.
- Each check can be switched off.

## Adding your own check

```csharp
public class PaymentGatewayCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => await Ping(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Gateway does not answer");
}

builder.Services.AddI25HealthCheck<PaymentGatewayCheck>("payment-gateway");   // timeout optional
```

It appears in `/ready` and `/details` automatically. Return `Degraded` for "works but worse" (stays in rotation).

## How it works

- Code: `i25Core.AspNetCore/Health/`. The checks use ASP.NET Core's built-in `IHealthCheck`, no extra package.
- The endpoints are minimal-API routes mapped after `MapControllers()` with `AllowAnonymous()`, so the
  secure-by-default policy does not block probes. `/details` uses the existing `AdminOnly` policy.
- Anonymous answers contain **only the overall status**: no check names, versions, paths or error text. Details,
  which can include server names from SQL errors, are for admins.
- Responses carry `Cache-Control: no-store`, and are hidden from Swagger.

## Decisions and limitations

- **Degraded returns 200**, so a missing job server or a pending migration does not pull the instance out of rotation.
  Watch `/details` (or an alert on it) if you want to know.
- A killed job server stays in Hangfire's table for minutes; only servers with a heartbeat in the last 2 minutes
  count as running.
- `UseHttpsRedirection` still applies: a probe that calls plain HTTP gets a redirect. Probe over HTTPS, or terminate
  TLS in front of the app (the usual setup).
- The anonymous endpoints can be called by anyone who can reach the app. If that is too open, block `/health` at the
  proxy and probe from inside the network.
- Mail is not checked: a failing SMTP server should not take the API out of rotation. Add your own check if you
  want it.
- Only readiness checks are exposed (no separate "startup" probe).

## Testing performed

Against a scratch database, restarting the app per scenario:

| Case | Result |
|---|---|
| live, ready and `/health`: 200 and only `{"status":"Healthy"}` for anonymous callers; `no-store` | passed |
| No probe file left in storage | passed |
| `/details`: no token 401, normal user 403, admin 200 with the three checks, timings and descriptions | passed |
| Missing migration: Degraded, still 200, details explain it | passed |
| Database taken offline: cached answer inside the window, 503 after it; `/live` stays 200; details show Unhealthy with the error | passed |
| Database back online: ready returns to 200 | passed |
| Job server off: Degraded (200), details say so | passed |
| Default disk not writable: 503, details name the storage problem | passed |
| Custom `Path`; old path gone; a check switched off disappears from details | passed |
| `Enabled=false` maps nothing; a bad `Path` stops the app at startup | passed |

Defect found while testing: a stopped job server still counted as running (its table row lives for minutes), so the
"no job server" warning could not appear. Only recent heartbeats count now.

Not covered: the per-check timeout path (a check that hangs) and a read-only database account.

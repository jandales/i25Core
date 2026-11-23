# Structured logging

Logs are now **events with fields**, not just text. Instead of `User 5 failed to sign in` you get an event with
`UserId = 5` that you can search, filter and count in any log tool (Seq, Elasticsearch/Kibana, Datadog, Loki, Azure
Monitor...). Built on [Serilog](https://serilog.net). Existing `ILogger<T>` code keeps working unchanged.

```csharp
_logger.LogInformation("Order {OrderId} paid by {UserId}", order.id, userId);   // OrderId and UserId become fields
```

Write the data as `{Placeholders}`, never with string interpolation (`$"..."`): placeholders keep the fields.

## What you get

- **Console**: readable text in Development, **one JSON object per line** everywhere else (what log collectors expect).
- **Rolling file**: `logs/log-20261123.json`, one file per day, 14 days kept, a new file when one reaches 100 MB.
- **One summary line per HTTP request**: method, path, status, time, user id, client IP, user agent.
- **Correlation id** on every request (`X-Correlation-Id`), attached to every log line of that request.
- **User id** on every log line written while an authenticated request runs.
- Startup failures (missing JWT key, bad configuration) are logged as a structured Fatal event, exit code 1.

Example (Production console / file, shortened):

```json
{"@t":"2026-11-23T07:30:40Z","@m":"HTTP \"GET\" \"/api/Users\" responded 200 in 12 ms","@tr":"85d77b1b82192e421bc833855f9caa41",
 "RequestMethod":"GET","RequestPath":"/api/Users","StatusCode":200,"Elapsed":12.4,"UserId":"7","ClientIp":"::1",
 "CorrelationId":"85d77b1b82192e421bc833855f9caa41","Application":"i25Core","Environment":"Production"}
```
(`@t` time, `@m` rendered message, `@l` level - left out for Information, `@x` exception, `@tr` trace id.)

## Finding what happened to one request

1. The client gets `X-Correlation-Id` in every response (and `traceId` inside error bodies).
2. Search your logs for `CorrelationId = "<value>"`: the summary line and every line the app wrote for that request.
3. A client can send its own `X-Correlation-Id` (8-64 characters of `A-Z a-z 0-9 . _ -`) to carry one id through
   several services. Anything else is ignored and replaced, so a caller cannot forge or inject log content.
4. The `traceId` in an error body looks like `00-<32 hex>-<16 hex>-01`; the 32 hex part is `@tr` in the logs.
   Without a client-supplied header, `CorrelationId` equals that same 32 hex value.

## Configuration

Log levels use the standard Serilog section:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "Hangfire": "Information"
    }
  }
}
```

Turn SQL logging on while debugging with `"Microsoft.EntityFrameworkCore.Database.Command": "Information"`.
Everything else is in `StructuredLogging`:

| Setting | Default | Meaning |
|---|---|---|
| `ConsoleFormat` | `Auto` | `Auto` = text in Development, JSON otherwise. Or `Text` / `Json`. |
| `File:Enabled` | `true` | Rolling file on or off. |
| `File:Path` | `logs/log-.json` | Relative to the app folder. The date is added before the extension. |
| `File:RetainedDays` | `14` | Files kept (counted in files, one per day). |
| `File:MaxFileSizeMb` | `100` | Start a new file at this size. `0` = no limit. |
| `Requests:Enabled` | `true` | The per-request summary line. |
| `Requests:CorrelationIdHeader` | `X-Correlation-Id` | Header name for the id. |
| `Requests:SlowRequestMs` | `2000` | Slower requests are logged as Warning. `0` = off. |
| `Requests:QuietPaths` | `/health /jobs /swagger /favicon.ico` | Logged only at Debug, so probes do not fill the log. |

Any setting can come from an environment variable, for example `StructuredLogging__File__Enabled=false` or
`Serilog__MinimumLevel__Default=Warning`. In containers, turn the file off and let the platform collect the console.

Request summary levels: 5xx or an exception = Error, quiet path = Debug, slow = Warning, everything else = Information.

## How it works

- Code: `i25Core.AspNetCore/Logging/LoggingServiceExtensions.cs`; package `Serilog.AspNetCore`.
- `AddI25Logging()` swaps the host's logging for Serilog. `UseI25CorrelationId()` and `UseI25RequestLogging()` come
  right after the security headers, so even 401s and errors are logged with their id. `UseI25LogUser()` runs after
  authentication and adds the user id to the remaining lines.
- Serilog adds the framework's own request scope (`RequestPath`, `RequestId`...) to the application's lines too.
- A bootstrap logger in `Program.cs` covers the time before the host exists.
- The request logger records **only the path**: no query string, headers, request or response bodies. Passwords, the
  `Authorization` token and `?token=` values never reach the log.
- The default `ExceptionHandlerMiddleware` log is set to Fatal because the app's `GlobalExceptionHandler` already
  logs every unhandled exception, with its trace id. Without this each crash printed its stack trace twice.

## Decisions and limitations

- **No request or response body logging**, by design (passwords, tokens, personal data). Log specific fields yourself.
- **Personal data in your own log calls is your responsibility.** Email addresses appear where the code logs them
  (for example the bootstrap administrator line). The user agent is cut at 120 characters.
- The file is plain local disk. Rotate and ship it with your platform if you need it elsewhere; the console is
  usually the better source in Docker or Kubernetes.
- The JSON uses Serilog's "compact" format (`@t`, `@m`, `@l`, `@x`, `@tr`), which Seq reads natively. Other tools can
  map those names, or you can change the formatter in `AddI25Logging`.
- Logging is synchronous (no async sink). Fine for typical APIs; add `Serilog.Sinks.Async` if you log at very high volume.
- Not included: a log shipper/sink for a specific service (Seq, Elastic...). Add the sink package and one line in
  `AddI25Logging`.
- Not changed: audit logging (database `AuditLog`) is separate and still records data changes.

## Testing performed

Against a scratch database, with a temporary controller (removed afterwards), restarting the app per scenario:

| Case | Result |
|---|---|
| Every console line is valid JSON in Production; text in Development; `ConsoleFormat=Json` forces JSON | passed |
| Request line has method, path, status, elapsed, client IP, user agent, application, environment, trace id | passed |
| Application's own line is structured (`Thing`, `Count` as fields) and carries the correlation id | passed |
| Correlation id: generated when absent, safe client value kept and echoed, 5 kinds of unsafe value replaced, nothing unsafe written to the log | passed |
| 500: one Error line with the exception and the same id; the response has no stack or message detail; response `traceId` matches `@tr` | passed |
| Business rule (422) logged as Warning; 401 logged as Information without a stack | passed |
| User id on the request line and on the application's line inside an authenticated request; none for anonymous | passed |
| Passwords, bearer token, query-string token never in console or file | passed |
| `/health` calls not logged at Information; EF SQL hidden; Hangfire still logs | passed |
| File sink holds the same events as the console; `File:Enabled=false` writes nothing | passed |
| Size roll (`..._001`...) and `RetainedDays` cleanup of old files | passed |
| `SlowRequestMs`, `MinimumLevel`, a namespace `Override`, `Requests:Enabled=false` | passed |
| Bootstrap-admin line has `Email` as its own field | passed |
| Bad `ConsoleFormat` and missing JWT key: Fatal event, exit code 1 | passed |

Defect found while testing: every unhandled exception was logged twice (the framework's middleware and our handler),
each with the full stack trace. The framework's copy is now silenced.

Known, not changed here: `Newtonsoft.Json 11.0.1` (a Hangfire dependency) is reported with a high-severity advisory by
`dotnet list package --vulnerable`. It was there before this feature; pinning `Newtonsoft.Json` 13.0.3 fixes it.

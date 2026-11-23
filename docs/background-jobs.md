# Background Jobs and Scheduler

i25Core can run work outside the HTTP request, and run work on a schedule. It uses [Hangfire](https://www.hangfire.io/) with **SQL Server storage**, so jobs live in your existing database, survive restarts, and are retried when they fail.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Run something in the background now | `IBackgroundJobService.Enqueue` | `dispatch(new Job)` |
| Run it later | `Schedule(…, delay)` / `Schedule(…, runAt)` | `->delay(now()->addMinutes(10))` |
| Run something after another job | `ContinueWith` | Job chaining |
| Run on a schedule (cron) | `IRecurringJob` + `AddRecurringJob<T>()` | `$schedule->job(new Job)->daily()` |
| See and manage jobs | Dashboard at `/jobs` | Horizon |
| Failed job retries | Automatic, configurable | `$tries` |

---

## Configuration

`appsettings.json`:

```json
"BackgroundJobs": {
  "ServerEnabled": true,
  "DashboardEnabled": true,
  "DashboardPath": "/jobs",
  "WorkerCount": 0,
  "Queues": [ "default" ],
  "RetryAttempts": 3,
  "SchedulePollingIntervalSeconds": 15,
  "SchemaName": "HangFire"
}
```

| Setting | Default | Description |
|---|---|---|
| `ServerEnabled` | `true` | Process jobs inside this app. Set `false` on instances that should only enqueue jobs. |
| `DashboardEnabled` | `true` | Serve the dashboard |
| `DashboardPath` | `/jobs` | Dashboard URL |
| `WorkerCount` | `0` | Parallel workers. `0` uses Hangfire's default (processor count × 5, maximum 20). |
| `Queues` | `["default"]` | Queues this server processes, highest priority first |
| `RetryAttempts` | `3` | Retries before a job is marked **Failed** |
| `SchedulePollingIntervalSeconds` | `15` | How often delayed and recurring jobs are checked |
| `SchemaName` | `HangFire` | SQL Server schema for Hangfire's tables |

**No migration is needed.** Hangfire creates its own tables in the `HangFire` schema of the same database the first time the app starts. EF Core migrations are not involved.

---

## 1. Running work in the background

Inject `IBackgroundJobService` (from `i25Core.Core.BackgroundJobs`) and point it at a method on a **job class**:

```csharp
public class WelcomeEmailJob
{
    private readonly I25DbContext _db;          // scoped services are fine
    private readonly IEmailSender _email;

    public WelcomeEmailJob(I25DbContext db, IEmailSender email) { _db = db; _email = email; }

    public async Task SendAsync(int vendorId)
    {
        var vendor = await _db.Vendors.FindAsync(vendorId);
        await _email.SendAsync(vendor!.Email, "Welcome!");
    }
}
```

Register it:

```csharp
builder.Services.AddScoped<WelcomeEmailJob>();
```

Dispatch it from a controller or business layer:

```csharp
public class VendorController : BaseController<Vendor>
{
    private readonly IBackgroundJobService _jobs;

    [HttpPost("{id}/welcome")]
    public IActionResult SendWelcome(int id)
    {
        _jobs.Enqueue<WelcomeEmailJob>(j => j.SendAsync(id));
        return OkResponse<object?>(null, "Email queued.");     // returns immediately
    }
}
```

### Methods

| Method | What it does |
|---|---|
| `Enqueue<T>(job, queue?)` | Run as soon as a worker is free. Returns the job id. |
| `Schedule<T>(job, TimeSpan delay, queue?)` | Run once after a delay |
| `Schedule<T>(job, DateTimeOffset runAt, queue?)` | Run once at a specific time |
| `ContinueWith<T>(parentJobId, job, queue?)` | Run after the parent job **succeeds** |
| `AddOrUpdateRecurring<T>(id, job, cron, timeZone?)` | Create or update a recurring job at run time |
| `RemoveRecurring(id)` | Remove a recurring job |
| `TriggerRecurring(id)` | Run a recurring job now |
| `Delete(jobId)` | Cancel a queued or scheduled job. Returns `false` if it already finished. |

```csharp
_jobs.Schedule<WelcomeEmailJob>(j => j.SendAsync(id), TimeSpan.FromMinutes(10));

var first = _jobs.Enqueue<ImportJob>(j => j.RunAsync(fileId));
_jobs.ContinueWith<NotifyJob>(first, j => j.DoneAsync(fileId));

_jobs.Enqueue<ReportJob>(j => j.BuildAsync(id), queue: "reports");
```

### Rules for job arguments

The call is **serialized to the database** and run later, possibly after a restart and possibly on another server. So:

| Do | Don't |
|---|---|
| Pass ids, strings, numbers, dates | Pass entities, `DbContext`, `HttpContext` or anything with navigation properties |
| Load fresh data inside the job | Assume the data is the same as when the job was queued |
| Make the job safe to run twice (idempotent), since a retry or a server crash can repeat it | Assume it runs exactly once |

The job class is resolved from DI in a **new scope** each time it runs, so constructor-injected scoped services such as `I25DbContext` work normally.

---

## 2. Scheduled (recurring) jobs

Implement `IRecurringJob`:

```csharp
using i25Core.Core.BackgroundJobs;

public class AuditCleanupJob : IRecurringJob
{
    private readonly I25DbContext _db;
    public AuditCleanupJob(I25DbContext db) => _db = db;

    public string Cron => "0 2 * * *";                       // every day at 02:00

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        await _db.AuditLogs
            .Where(a => a.ChangedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
```

Register it in `Program.cs`. It is scheduled automatically at startup:

```csharp
builder.Services.AddRecurringJob<AuditCleanupJob>();
```

### Optional settings on the job

| Member | Default | Purpose |
|---|---|---|
| `Cron` | required | When to run |
| `TimeZone` | UTC | e.g. `TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time")` |
| `Queue` | `"default"` | Queue to run on |

### Cron cheat sheet

| Expression | Meaning |
|---|---|
| `* * * * *` | Every minute |
| `*/15 * * * *` | Every 15 minutes |
| `0 * * * *` | Every hour |
| `0 2 * * *` | Daily at 02:00 |
| `0 9 * * 1-5` | Weekdays at 09:00 |
| `0 0 1 * *` | First day of every month, midnight |
| `*/30 * * * * *` | Every 30 seconds (6 fields, seconds first) |

Changing `Cron` in code takes effect the next time the app starts. The job's id is its **class name**, which is also the name shown in the dashboard. Renaming the class creates a new job; the old schedule stays until you remove it in the dashboard.

---

## 3. Dashboard

Open `http://localhost:{port}/jobs`. It shows queued, scheduled, processing, succeeded and failed jobs, recurring jobs with their next run time, and server status. You can retry, delete or trigger jobs from it.

**Security:** by default the dashboard answers **local requests only**, so it is not reachable from other machines. Before you expose it, either keep `DashboardEnabled` off in production, or add an authorization filter once authentication exists.

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/BackgroundJobs/IBackgroundJobService.cs` | **New.** Dispatch interface, with no dependency on Hangfire |
| `i25Core.Core/BackgroundJobs/IRecurringJob.cs` | **New.** Contract for scheduled jobs |
| `i25Core.Infrastructure/BackgroundJobs/BackgroundJobOptions.cs` | **New.** `BackgroundJobs` settings |
| `i25Core.Infrastructure/BackgroundJobs/HangfireBackgroundJobService.cs` | **New.** `IBackgroundJobService` on Hangfire's client |
| `i25Core.Infrastructure/BackgroundJobs/RecurringJobRunner.cs` | **New.** What Hangfire stores for each `IRecurringJob`; resolves the job from a fresh DI scope |
| `i25Core.AspNetCore/BackgroundJobs/BackgroundJobServiceExtensions.cs` | **New.** `AddI25BackgroundJobs`, `AddRecurringJob<T>`, `UseI25BackgroundJobs` |
| `i25Core.AspNetCore/BackgroundJobs/RecurringJobRegistrar.cs` | **New.** Hosted service that schedules every registered `IRecurringJob` at startup |
| `i25Core.Web/Program.cs` | Registration and dashboard |
| `i25Core.Web/appsettings.json` | `BackgroundJobs` section |
| `*.csproj` | `Hangfire.Core`, `Hangfire.SqlServer` (Infrastructure); `Hangfire.AspNetCore` (Web) at 1.8.14 |

### Flow

```
Your code                                   Database (HangFire schema)           Job server (same app)
─────────                                   ──────────────────────────           ─────────────────────
_jobs.Enqueue<MyJob>(j => j.Run(5))
   │  serialize: type + method + args  ───►  INSERT Job, push to queue 'default'
   │  (returns the job id at once)                     │
   ▼                                                   ▼
HTTP response sent                                worker polls ───────────────►  new DI scope
                                                                                  resolve MyJob
                                                                                  MyJob.Run(5)
                                                  state → Succeeded  ◄───────────  done
                                                  or Failed → retry (1/3, 2/3 …)

Startup:  RecurringJobRegistrar ─► for each AddRecurringJob<T>():
              AddOrUpdate("T", runner.RunAsync("T", typeName), cron, timeZone)
          Hangfire scheduler enqueues it each time the cron is due
```

### Design decisions

| Decision | Reason |
|---|---|
| **Hangfire with SQL Server storage** | The app already uses SQL Server, so there is no extra infrastructure (no Redis or message broker). Jobs persist across restarts and have a dashboard. |
| **`IBackgroundJobService` in Core** | Business code depends on a small interface, not on Hangfire, so it can be tested and the technology can change |
| **The job server runs inside the web app** | Simplest setup. Set `ServerEnabled: false` on some instances to split web and worker roles later; they share the same database. |
| **Recurring jobs as a class + registration** | Mirrors Laravel's scheduler: the schedule lives with the job, and `AddRecurringJob<T>()` is one line |
| **`RecurringJobRunner` indirection** | Hangfire stores a method call. Routing every recurring job through one runner gives each job a fresh DI scope with no per-job plumbing. |
| **Dashboard local-only by default** | Safe by default; it shows job arguments and can delete jobs |
| **`PrepareSchemaIfNecessary`** | No manual SQL script or migration; works on first run |
| **Lowercase queue names** | Hangfire accepts only lowercase letters, digits, `_` and `-` |
| **Default retries = 3** | Handles temporary failures such as network or database blips without retrying forever |

---

## Limitations

| Limitation | Notes |
|---|---|
| Jobs run **at least once** | Retries or a crashed server can repeat a job. Design jobs to be idempotent. |
| Timing is limited by the polling interval | Delayed and recurring jobs are checked every `SchedulePollingIntervalSeconds` (15 s by default). A cron such as every 3 seconds will not fire more often than that. Lower the setting if you need finer timing. |
| `Enqueue` returns before the job runs | Return a job id or a message, not the job's result. For results, write them to a table. |
| Dashboard has no login | Local only until authentication is added |
| Hangfire tables live in your main database | Heavy job traffic shares the database. A separate database or schema is possible by changing the connection string passed to `AddI25BackgroundJobs`. |
| Hangfire's built-in retry waits grow between attempts | The first retry takes roughly 15–45 seconds. This is Hangfire's default back-off. |

---

## Testing performed

These were run against the API on a temporary SQL Server database, with temporary test jobs (a job that reads `I25DbContext`, a failing job, and a recurring job) and a temporary controller to trigger them. All of it was removed afterwards.

| # | Case | Result |
|---|---|---|
| 1 | First start | Hangfire created its `HangFire` tables automatically; recurring job registered |
| 2 | Dashboard `GET /jobs` from the local machine | 200 |
| 3 | `Enqueue` a job that uses scoped `I25DbContext` | Ran within seconds, database access worked |
| 4 | `Schedule` with a 4 s delay | Did not run after 2 s; ran once afterwards |
| 5 | `ContinueWith` | Child job ran right after the parent succeeded |
| 6 | `Delete` a scheduled job | Returned `true`; job state **Deleted**, never ran |
| 7 | Recurring `IRecurringJob` registered at startup | Listed as a recurring job and executed on schedule (timing bounded by the 15 s polling interval) |
| 8 | `AddOrUpdateRecurring` + `TriggerRecurring` + `RemoveRecurring` | Triggered run happened; job removed |
| 9 | Job that always throws | Attempted once, then state **Scheduled** with "Retry attempt 1 of 3" |
| 10 | Final database state | 9 Succeeded, 1 Deleted, 1 Scheduled (retry) |

# Events and Listeners

Events let one part of the app announce that something happened, and other parts react without the first part knowing about them. A vendor is approved; separate listeners send an email, write a log entry and refresh a cache.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Announce that something happened | `IEventDispatcher.DispatchAsync(new MyEvent(...))` | `event(new MyEvent(...))` |
| React to it | A class implementing `IEventListener<MyEvent>` | A Listener class |
| React to a record being saved | `EntityCreatedEvent<T>`, `EntityUpdatedEvent<T>`, `EntityDeletedEvent<T>`, raised automatically | Model events (`created`, `updated`, `deleted`) |
| React in the background | `[Queued]` on the listener | `implements ShouldQueue` |
| Register listeners | Automatic, by assembly scan | `EventServiceProvider::$listen` / auto-discovery |

There is no MediatR dependency. Newer MediatR versions are commercial-licensed, and this built-in dispatcher covers what the boilerplate needs.

---

## Quick start

### 1. Define an event

```csharp
using i25Core.Core.Events;

public class VendorApproved : IEvent
{
    public int VendorId { get; set; }
    public string? ApprovedBy { get; set; }
}
```

### 2. Write listeners

```csharp
public class SendVendorWelcomeEmail : IEventListener<VendorApproved>
{
    private readonly IEmailSender _email;
    public SendVendorWelcomeEmail(IEmailSender email) => _email = email;

    public Task HandleAsync(VendorApproved e, CancellationToken ct) =>
        _email.SendAsync(e.VendorId, "Welcome!");
}

public class RefreshVendorCache : IEventListener<VendorApproved>
{
    private readonly ICacheService _cache;
    public RefreshVendorCache(ICacheService cache) => _cache = cache;

    public Task HandleAsync(VendorApproved e, CancellationToken ct) =>
        _cache.RemoveAsync("vendors:active", ct);
}
```

Listeners are registered automatically. They can inject any service, including `I25DbContext`.

### 3. Dispatch the event

```csharp
public class VendorController : BaseController<Vendor>
{
    private readonly IEventDispatcher _events;

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        await _service.Approve(id);
        await _events.DispatchAsync(new VendorApproved { VendorId = id });
        return OkResponse<object?>(null, "Approved.");
    }
}
```

Every listener of `VendorApproved` runs, one after another, before `DispatchAsync` returns.

---

## Automatic model events

When a `BaseEntity` is saved, i25Core raises an event for it. Nothing is needed in controllers or business layers.

| Event | When | Useful data |
|---|---|---|
| `EntityCreatedEvent<T>` | A row was inserted | `Entity`, with `Entity.id` already set by the database |
| `EntityUpdatedEvent<T>` | A row was changed | `Entity`, and `ChangedProperties`, the names of the fields that changed |
| `EntityDeletedEvent<T>` | A row was deleted | `Entity`, still holding its last values |

```csharp
public class NotifyOnNewVendor : IEventListener<EntityCreatedEvent<Vendor>>
{
    public Task HandleAsync(EntityCreatedEvent<Vendor> e, CancellationToken ct)
    {
        // e.Entity.id is the real id here
        return Task.CompletedTask;
    }
}

public class WatchCreditLimit : IEventListener<EntityUpdatedEvent<Vendor>>
{
    public Task HandleAsync(EntityUpdatedEvent<Vendor> e, CancellationToken ct)
    {
        if (e.ChangedProperties.Contains(nameof(Vendor.CreditLimit))) { /* ... */ }
        return Task.CompletedTask;
    }
}
```

### Listening more broadly

Model events inherit from each other, so one listener can cover a whole family:

| Listen to | Hears |
|---|---|
| `EntityCreatedEvent<Vendor>` | Only new vendors |
| `EntityEvent<Vendor>` | Every create, update and delete of a vendor |
| `EntityEvent` | Every create, update and delete of **every** entity |

```csharp
public class LogEverything : IEventListener<EntityEvent>
{
    public Task HandleAsync(EntityEvent e, CancellationToken ct)
    {
        Console.WriteLine($"{e.GetType().Name} on {e.EntityType.Name}");
        return Task.CompletedTask;
    }
}
```

### When they fire

- **After the save has committed.** The row exists, its id is set, and the data can no longer be rolled back.
- **Only for real changes.** An update that changes nothing, or only the housekeeping columns (`CreateAt`, `ModifiedAt`, `intConcurrencyId`), raises no event.
- **Not for the audit tables.** Audit rows written by audit logging do not raise events.

### Turning them off for an entity

```csharp
[NoModelEvents]
public class ImportStaging : BaseEntity { ... }
```

---

## Queued listeners (run in the background)

Add `[Queued]` to a listener to run it through the background job queue instead of during the request. The request returns without waiting for it.

```csharp
[Queued(Queue = "emails")]                 // Queue is optional; default is "default"
public class SendVendorWelcomeEmail : IEventListener<VendorApproved> { ... }
```

| | Inline listener | `[Queued]` listener |
|---|---|---|
| Runs | During `DispatchAsync` | Later, on a job worker |
| Slows the request | Yes | No |
| Survives a restart | No | Yes, it is stored in the database |
| Retried on failure | No | Yes, automatically (see background jobs) |
| Event data | The live object | A **JSON copy** taken at dispatch time |

Rules for queued listeners:

- **The event must serialize to JSON.** Use simple properties such as ids, strings, numbers and dates. For model events, the entity is serialized as it was at save time.
- **The queue name must be processed by the job server.** List it under `BackgroundJobs:Queues` in `appsettings.json`, otherwise the job waits forever.
- **Requires background jobs.** If `AddI25BackgroundJobs` isn't registered, a warning is logged and the listener runs inline.
- **Make it safe to run twice.** A retry can repeat it.
- The listener is created in a fresh DI scope when the job runs, so it can use `I25DbContext`.

---

## Configuration

`appsettings.json`:

```json
"Events": {
  "ThrowOnListenerError": false,
  "MaxDepth": 10
}
```

| Setting | Default | Description |
|---|---|---|
| `ThrowOnListenerError` | `false` | `false`: a failing listener is logged and the others still run. `true`: after all listeners ran, `DispatchAsync` throws an `AggregateException` (shown as a 500 from an API call). |
| `MaxDepth` | `10` | How deeply events may trigger more events. Stops infinite loops. |

Registration in `Program.cs` (already in place):

```csharp
builder.Services.AddI25Events(builder.Configuration, typeof(Program).Assembly);
```

Pass more assemblies if listeners live in other projects:
`AddI25Events(config, typeof(Program).Assembly, typeof(MyListeners).Assembly)`.

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/Events/EventContracts.cs` | **New.** `IEvent`, `IEventListener<T>`, `IEventDispatcher` |
| `i25Core.Core/Events/EventAttributes.cs` | **New.** `[Queued]`, `[NoModelEvents]` |
| `i25Core.Core/Events/EntityEvents.cs` | **New.** `EntityEvent`, `EntityEvent<T>`, `EntityCreatedEvent<T>`, `EntityUpdatedEvent<T>`, `EntityDeletedEvent<T>` |
| `i25Core.Infrastructure/Events/EventDispatcher.cs` | **New.** Finds listeners in DI and runs or queues them |
| `i25Core.Infrastructure/Events/QueuedListenerRunner.cs` | **New.** What the job queue runs for a `[Queued]` listener |
| `i25Core.Infrastructure/Events/ModelEventCapture.cs` | **New.** Reads the change tracker and builds the model events |
| `i25Core.Infrastructure/Events/EventSupport.cs` | **New.** Shared helpers: calling a listener, JSON options, version-free type names |
| `i25Core.Infrastructure/Events/EventOptions.cs` | **New.** `Events` settings |
| `i25Core.Infrastructure/I25DbContext.cs` | Captures model events before saving and dispatches them after |
| `i25Core.Web/Events/EventServiceExtensions.cs` | **New.** `AddI25Events` scans and registers listeners |
| `i25Core.Web/Program.cs`, `appsettings.json` | Registration and settings |

### Dispatch flow

```
await _events.DispatchAsync(new VendorApproved { ... })
   │
   ▼  for the event's type, then each base type (VendorApproved, then its base classes...)
   │     look up IEventListener<thatType> in DI  ──► every registered listener
   │
   ├─ plain listener   → HandleAsync(event) now, in order
   │                      (a throw is logged; the next listener still runs)
   │
   └─ [Queued] listener → serialize the event to JSON
                          → IBackgroundJobService.Enqueue<QueuedListenerRunner>(...)
                                   │
                                   ▼  later, on a job worker
                              new DI scope → create listener → rebuild event from JSON → HandleAsync
```

### Model event flow

```
SaveChangesAsync()
 │
 ├─ ModelEventCapture.Capture(ChangeTracker)   ◄─ BEFORE saving: states are still known
 │     Added → EntityCreatedEvent<T>   Modified → EntityUpdatedEvent<T> (+ ChangedProperties)
 │     Deleted → EntityDeletedEvent<T>
 │
 ├─ save data + audit rows in one transaction   (existing audit logging)
 │
 └─ for each captured event: _events.DispatchAsync(event)   ◄─ AFTER commit: ids are set
```

### Design decisions

| Decision | Reason |
|---|---|
| **Own dispatcher, no MediatR** | MediatR's newer versions need a commercial licence. The dispatcher is about 100 lines and does what the boilerplate needs. |
| **Listeners found by assembly scan** | A new listener needs no registration: write the class and it works, like Laravel's event auto-discovery |
| **Dispatch model events after commit** | Listeners see the saved row with its real id, and a rolled-back save never announces anything |
| **A failing listener doesn't stop the others (default)** | One broken listener, such as a failed email, shouldn't block the rest or fail the user's request. Turn on `ThrowOnListenerError` for strict behaviour. |
| **Listeners are resolved when dispatching, not at startup** | They get the request's scoped services, and the `DbContext` can depend on the dispatcher without a circular dependency |
| **Listeners also run for base event types** | `EntityEvent<Vendor>` and `EntityEvent` give broad listeners without extra code |
| **`[Queued]` reuses the background jobs feature** | One queue, one dashboard, one retry policy. No second system. |
| **Queued events are JSON copies** | A queued job may run later or on another server, so it can't share the live object |
| **Version-free type names in queued jobs** | A job queued before a redeploy still finds its classes after the assembly version changes |
| **Depth limit** | A listener that triggers its own event would otherwise loop until the server crashes |
| **Optional dispatcher in `I25DbContext`** | The context still works when events aren't registered, for example in EF design-time tools |

---

## Limitations

| Limitation | Notes |
|---|---|
| Model events come only from EF saves | Raw SQL, stored procedures and `ExecuteUpdate` / `ExecuteDelete` bypass the change tracker and raise nothing |
| With your own outer transaction, events fire when `SaveChanges` returns | That is before your transaction commits. Dispatch manually after your own commit if listeners must see committed data. |
| Inline listeners run one after another | Order follows registration order and isn't guaranteed. Don't depend on it. |
| A listener that saves data raises its own model events | This works (tested). The depth limit protects against loops. |
| Queued model events carry a snapshot | `Entity` reflects the row at save time, not at the time the job runs. Reload it by id if you need current data. |
| The sync `SaveChanges()` waits for async listeners | Prefer `SaveChangesAsync()` |
| Listener failures are only logged by default | Watch the logs, or enable `ThrowOnListenerError` |

---

## Testing performed

These were run against the API on a temporary SQL Server database, with temporary entities, listeners and controllers. All of it was removed afterwards.

| # | Case | Result |
|---|---|---|
| 1 | `POST` a new row | `EntityCreatedEvent<T>` listener ran with the real database id; the `EntityEvent<T>` and `EntityEvent` listeners also ran; a `[Queued]` listener ran on a worker thread |
| 2 | `PUT` changing only `Name` | `EntityUpdatedEvent<T>` with `ChangedProperties = [Name]` |
| 3 | `PUT` with no real change | No events |
| 4 | `PUT` changing `Name` and `Price` | `ChangedProperties = [Name, Price]` |
| 5 | `DELETE` | `EntityDeletedEvent<T>` with the last values |
| 6 | Create on a `[NoModelEvents]` entity | No events |
| 7 | Audit log rows | Did not raise events |
| 8 | Manual dispatch with a failing listener | Request returned 200; the other listeners and a `[Queued]` one still ran; the failure was logged |
| 9 | A listener that saves another entity | Nested `EntityCreatedEvent` fired with its own id |
| 10 | A listener that dispatches its own event | Stopped at depth 10; error logged |
| 11 | `ThrowOnListenerError = true` | Request returned 500 with the real message ("listener boom"); all listeners still ran first |
| 12 | A listener resolving `I25DbContext`, inline and queued | Worked in both |

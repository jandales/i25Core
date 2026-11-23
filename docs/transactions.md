# Transactions / Unit of work

Run several saves as one all-or-nothing unit, like Laravel's `DB::transaction()`.

Before this feature every `SaveChangesAsync` was its own transaction (plus the audit rows, which already share
the save's transaction). If a service saved twice and the second save failed, the first stayed in the database.

## Usage

Inject `IUnitOfWork` (`i25Core.Core.Transactions`).

### 1. A delegate (preferred)

```csharp
await _uow.ExecuteAsync(async ct =>
{
    _db.Orders.Add(order);
    await _db.SaveChangesAsync(ct);

    stock.Quantity -= order.Quantity;
    await _db.SaveChangesAsync(ct);          // throws -> the order above is rolled back too
}, cancellationToken);
```

It commits when the delegate returns and rolls back when it throws. It can return a value
(`var id = await _uow.ExecuteAsync(async ct => { ...; return order.id; })`) and takes an isolation level:
`_uow.ExecuteAsync(IsolationLevel.Serializable, async ct => { ... })`.

### 2. A scope you finish yourself

```csharp
await using var scope = await _uow.BeginAsync();
...saves...
await scope.CommitAsync();                   // without this line, disposing rolls back
```

Use it when the work cannot live in one delegate (for example an action filter). It does not work with a
retrying execution strategy; `ExecuteAsync` does.

### 3. On a controller action

```csharp
[Transactional]                              // or [Transactional(IsolationLevel.Serializable)]
[HttpPost]
public async Task<IActionResult> Place(OrderRequest request) { ... }
```

The whole action is one transaction. It commits on a success result and rolls back when the action throws or
returns a 4xx/5xx result. It can also sit on a controller class.

### After-commit work

Things that cannot be undone (queueing a background job, sending an email) must not happen if the transaction
rolls back, and a background job must not start before the data is committed:

```csharp
await _uow.ExecuteAsync(async ct =>
{
    ...saves...
    _uow.OnCommitted(_ => { _jobs.Enqueue<SendInvoiceJob>(j => j.Run(order.id)); return Task.CompletedTask; });
});
```

Callbacks run in order after the outermost commit and never after a rollback. Outside a transaction `OnCommitted`
runs the callback at once.

### Nesting

Calls can be nested. The outermost one owns the real transaction; inner ones join it. If an inner call throws,
the exception travels up and everything rolls back. If code catches the inner exception and carries on, the
outer commit refuses (`InvalidOperationException`) and rolls back, so a half-finished unit can never commit.

## How it works

- `UnitOfWork` (Infrastructure, scoped, one per request) wraps EF's `Database.BeginTransaction`, with a depth
  counter for nesting. `ExecuteAsync` runs inside EF's execution strategy, so it also works if you turn on
  `EnableRetryOnFailure` later.
- **Model events are deferred.** Inside a unit of work the context queues the events (`EntityCreatedEvent<T>` ...)
  and the unit fires them after the commit. Before this, listeners ran straight after each save, so they could
  see data that was later rolled back and could act on it. Outside a unit of work nothing changed: events fire
  right after the save.
- Audit rows are written in the same transaction as the data, as before. A rollback removes them too.
- After a rollback the context's change tracker is cleared, because the tracked entities no longer match the
  database (for example generated ids that were never committed). Reload what you need.
- The three multi-step operations in `AuthService` (refresh-token rotation, change password, reset password)
  now use the unit of work instead of opening transactions by hand.

## Decisions and limitations

- **No savepoints.** A nested call cannot roll back alone; the choice is all or nothing.
- **Isolation level** is applied only by the outermost call; an inner call's level is ignored.
- **One database.** There is no distributed transaction across several databases or services.
- A transaction that someone opened directly with `_db.Database.BeginTransaction()` is not taken over:
  `IUnitOfWork` throws instead of silently committing something it does not own.
- Callbacks given to `OnCommitted` run after the commit. If one throws, the data is already saved and the
  exception reaches the caller; make them safe to retry.
- `[Transactional]` treats any `IStatusCodeActionResult` with status 400 or above as a failure. Actions that
  signal errors only by throwing (the `AppException` types, handled by the global handler) roll back as well.

## Testing performed

Run against a scratch database with a temporary controller (removed afterwards):

| Case | Result |
|---|---|
| Two saves in one unit commit together | passed |
| An exception rolls back the earlier save and its audit rows | passed |
| `OnCommitted` runs after the commit, never after a rollback; immediate outside a transaction | passed |
| Model events fire after the transaction has closed, and not at all after a rollback | passed |
| Nested: inner joins; inner throws rolls back all; inner failure swallowed makes the outer refuse to commit | passed |
| `IsolationLevel.Serializable` really reaches the session | passed |
| Scope disposed without commit rolls back, and leaves no open transaction | passed |
| `[Transactional]`: commit, rollback on exception, rollback on 400, with an isolation level | passed |
| A normal request after the failures above works (no leaked state) | passed |
| Auth regression: register, login, refresh rotation, reuse detection, 5 parallel refreshes (exactly one wins), change password, reset password (saved, token single-use) | passed |

Defect found while testing: `[Transactional]` without arguments crashed (a null filter argument is not allowed);
it now passes `IsolationLevel.Unspecified` to mean "database default".

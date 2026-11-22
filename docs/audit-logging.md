# Automatic Audit Logging

Every insert, update and delete on an entity that inherits `BaseEntity` is recorded automatically. The log stores which entity changed, the action, when it happened, and the old and new value of each field that changed.

Controllers and business layers need no code for this. Any save through `I25DbContext.SaveChanges` / `SaveChangesAsync` is audited, which covers everything that goes through `BaseBusinessLayer`.

---

## What gets recorded

### Tables

**`AuditLog`**: one row per changed entity per save

| Column | Example | Notes |
|---|---|---|
| `AuditLogId` | `42` | Primary key |
| `EntityType` | `Vendor` | Entity class name |
| `EntityId` | `17` | Primary key of the changed row (the generated id on inserts) |
| `Action` | `Created` / `Updated` / `Deleted` | **New column** |
| `ChangedAt` | `2026-11-22T13:09:58Z` | UTC |
| `ChangedBy` | `0` | User id. Always `0` until authentication is added. |
| `ChangedByName` | `null` | User name, filled once authentication exists |
| `Remarks` | `null` | Free text, not set automatically |

**`AuditLogChange`**: one row per changed field

| Column | Example | Notes |
|---|---|---|
| `AuditLogId` | `42` | Parent `AuditLog` |
| `PropertyName` | `Name` | **New column.** The field that changed. |
| `ChangeType` | `Updated` | Same as the parent `Action` |
| `OriginalValue` | `Widget` | Value before the change (`null` on insert) |
| `NewValue` | `Widget Pro` | Value after the change (`null` on delete) |

### Per action

| Action | Fields logged | `OriginalValue` | `NewValue` |
|---|---|---|---|
| **Created** | Every field with a value | `null` | New value |
| **Updated** | Only fields whose value actually changed | Old value | New value |
| **Deleted** | Every field that had a value | Last value | `null` |

### Always skipped

- The primary key, which is stored in `AuditLog.EntityId` instead
- Housekeeping columns: `CreateAt`, `ModifiedAt`, `intConcurrencyId`
- The audit tables themselves, so audit rows don't audit each other
- Any property or class marked `[AuditIgnore]`
- Updates where nothing but skipped fields changed. These write no audit row at all.

### Value format

Values are stored as text, formatted with the invariant culture:

| Type | Stored as |
|---|---|
| Text | as is |
| Numbers | `1234.5`. Decimals have trailing zeros removed, so `9.50` is stored as `9.5`. |
| Dates | ISO 8601 round-trip: `2026-11-22T13:09:58.2668480Z` |
| Booleans | `True` / `False` |
| Guid / enum | `ToString()` |

---

## Usage

### Reading the trail

The existing endpoint returns an entity's history, newest first, with the field changes included:

```http
GET /api/AuditLogContoller/{entityType}/{entityId}
GET /api/AuditLogContoller/Vendor/17
```

```json
{
  "success": true,
  "data": [
    {
      "auditLogId": 2,
      "entityType": "Vendor",
      "entityId": 17,
      "action": "Updated",
      "changedAt": "2026-11-22T13:09:58.05",
      "changedBy": 0,
      "auditLogChanges": [
        { "propertyName": "Name", "originalValue": "Acme", "newValue": "Acme Ltd", "changeType": "Updated" }
      ]
    },
    {
      "auditLogId": 1,
      "entityType": "Vendor",
      "entityId": 17,
      "action": "Created",
      "auditLogChanges": [
        { "propertyName": "Name", "originalValue": null, "newValue": "Acme", "changeType": "Created" },
        { "propertyName": "CreditLimit", "originalValue": null, "newValue": "5000", "changeType": "Created" }
      ]
    }
  ]
}
```

`/api/AuditLogContoller/get` also works with the standard filters and sorting, for example to list everything deleted today:

```json
[
  { "column": "Action", "condition": "=", "value": "Deleted", "conjunction": "AND" },
  { "column": "ChangedAt", "condition": ">=", "value": "2026-11-22" }
]
```

### Excluding fields or entities

Use `[AuditIgnore]` from `i25Core.Core.Auditing`:

```csharp
using i25Core.Core.Auditing;

public class User : BaseEntity
{
    public string? Email { get; set; }

    [AuditIgnore]                      // never write secrets to the audit log
    public string? PasswordHash { get; set; }
}

[AuditIgnore]                          // skip the whole entity
public class LoginAttempt : BaseEntity { ... }
```

### Making a new entity audited

Inherit from `BaseEntity`. That's all.

```csharp
public class Warehouse : BaseEntity
{
    public string? Code { get; set; }
    public string? Name { get; set; }
}
```

---

## How it is implemented

### Files

| File | Change |
|---|---|
| `i25Core.Infrastructure/Auditing/AuditTrail.cs` | **New.** Captures pending changes and builds the `AuditLog` / `AuditLogChange` rows |
| `i25Core.Infrastructure/I25DbContext.cs` | `SaveChanges` and `SaveChangesAsync` now capture, save, then write the audit rows inside one transaction |
| `i25Core.Core/Auditing/AuditIgnoreAttribute.cs` | **New.** Opt-out attribute |
| `i25Core.Core/Entities/AuditLog.cs` | Added `Action` |
| `i25Core.Core/Entities/AuditLogChange.cs` | Added `PropertyName` |
| `Migrations/…_AddAuditActionAndPropertyName.cs` | **New.** Adds the two columns (`nvarchar(20)`, `nvarchar(128)`) |

### Save flow

```
SaveChangesAsync()
 │
 ├─ IncrementConcurrencyIds()                (existing)
 ├─ set CreateAt / ModifiedAt                (existing)
 │
 ├─ AuditTrail.Capture(ChangeTracker)        ◄─ BEFORE saving
 │     for each Added / Modified / Deleted BaseEntity:
 │       • read original + current values while EF still has both
 │       • keep only real changes; skip PK, housekeeping, [AuditIgnore]
 │     → list of PendingAudit (entry, action, changes)
 │
 ├─ nothing to audit?  → base.SaveChangesAsync() and return
 │
 ├─ BEGIN TRANSACTION   (unless the caller already opened one)
 ├─ base.SaveChangesAsync()                  ◄─ save #1: the data
 │     inserts now have their database-generated ids
 ├─ AuditTrail.Complete(pending)             ◄─ build AuditLog rows
 │     EntityId read from the entry *after* save #1
 ├─ AuditLogs.AddRange(...)
 ├─ base.SaveChangesAsync()                  ◄─ save #2: the audit rows
 └─ COMMIT
```

### Why two steps (capture, then complete)

- **Original values exist only before the save.** After `SaveChanges`, EF sets `OriginalValue = CurrentValue`, which would erase the "before" picture. So the changes are captured first.
- **Generated ids exist only after the save.** For an insert, the row's `id` is temporary until SQL Server assigns it. So `EntityId` is read after save #1.

### Why one transaction

Save #1 and save #2 run in the same database transaction. If writing the audit rows fails, the data change rolls back too, so a change can never exist without its audit record. If the caller already started a transaction, the audit rows join it and the caller decides when to commit.

### Why the base `SaveChanges` is used for save #2

Save #2 calls `base.SaveChangesAsync()` directly. That skips the overridden method, so the new audit rows are not captured and audited themselves. The audit tables are also excluded explicitly in `IsAudited()` as a second guard.

### Detecting real changes

```csharp
case Updated:
    if (!property.IsModified) continue;
    original = Format(property.OriginalValue);
    current  = Format(property.CurrentValue);
    if (original == current) continue;   // marked modified but value unchanged
```

`BaseBusinessLayer.UpdateSync` loads the row and applies the client's values with `SetValues`. EF marks only properties whose values differ, and the string comparison catches any that are flagged but equal. An update that changes nothing produces no audit row.

---

## Limitations

| Limitation | Notes |
|---|---|
| `ChangedBy` is always `0` | Fills in once authentication and a current-user service are added (roadmap #7–#9) |
| Only `BaseEntity` types | Other entities (for example owned or keyless types) aren't audited |
| `EntityId` is `int` only | Entities with composite or non-int keys are logged with `EntityId = 0` |
| Changes outside EF's change tracker aren't seen | Raw SQL, stored procedures (`ExecuteStoredProcedureAsync`), `ExecuteUpdate` / `ExecuteDelete` |
| Two database round trips per audited save | The data save plus the audit save, in one transaction |
| Values stored as text | Good for display and search, not meant for exact type round-tripping |

---

## Testing performed

This was tested against the API on a temporary SQL Server database with a temporary `AuditProbe` entity (`Name`, `Price`, and an `[AuditIgnore] Secret`). The temporary entity and database were removed afterwards.

| Step | Expected | Result |
|---|---|---|
| POST `{ name: "Widget", price: 9.5, secret: "hunter2" }` | `Created` with `Name`, `Price`; no `Secret`; `EntityId` = new id | ✓ |
| PUT changing `Name` only | `Updated` with one change: `Name` "Widget" → "Widget Pro" | ✓ |
| PUT changing only `Secret` (ignored) | No audit row | ✓ |
| DELETE | `Deleted` with last values of `Name` and `Price` | ✓ |
| Decimal update 12.5 → 15.25 | Logged as `12.5` → `15.25` (no trailing zeros) | ✓ |
| `GET /api/AuditLogContoller/AuditProbe/{id}` | Full history, newest first, with changes | ✓ |

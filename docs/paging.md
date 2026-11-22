# Paging

The `/get` endpoint on every controller that inherits `BaseController<T>` can return results one page at a time, similar to Laravel's `->paginate()`.

Paging is **opt-in**. It applies only when the request includes `Page` or `PageSize`. Without them, `/get` returns every matching row, as it did before paging existed.

---

## Usage

### Query parameters

| Parameter | Type | Default | Rules |
|---|---|---|---|
| `Page` | int | `1` | 1-based page number; must be ≥ 1 |
| `PageSize` | int | `20` | Must be ≥ 1; values above `100` are capped at `100` |
| `Filters` | JSON | — | Existing filter list (applied before paging) |
| `Sorts` | JSON | — | Existing sort list (applied before paging) |

### Examples

```http
GET /api/AuditLogContoller/get?Page=1&PageSize=10
```

```http
GET /api/AuditLogContoller/get?Page=2
```
This uses the default page size of 20.

```http
GET /api/AuditLogContoller/get?Page=1&PageSize=5
    &Filters=[{"column":"EntityType","value":"Order","condition":"="}]
    &Sorts=[{"column":"ChangedAt","direction":"desc"}]
```
URL-encode the `Filters` and `Sorts` values in real requests.

### Response

A paged request returns a `pagination` block next to `data`:

```json
{
  "success": true,
  "message": "Records retrieved successfully.",
  "data": [ /* up to PageSize rows */ ],
  "errors": null,
  "traceId": null,
  "pagination": {
    "page": 1,
    "pageSize": 10,
    "totalCount": 25,
    "totalPages": 3,
    "hasPrevious": false,
    "hasNext": true
  }
}
```

| Field | Meaning |
|---|---|
| `page` | The page returned |
| `pageSize` | Rows per page actually used (after the 100 cap) |
| `totalCount` | Rows matching the filters across **all** pages |
| `totalPages` | `ceil(totalCount / pageSize)` |
| `hasPrevious` | `page > 1` |
| `hasNext` | `page < totalPages` |

When the request is not paged, `pagination` is `null`.

### Errors

| Request | Status | Message |
|---|---|---|
| `Page=0` or negative | 400 | `Page must be a whole number greater than 0.` |
| `PageSize=abc` | 400 | `PageSize must be a whole number greater than 0.` |
| `Page` beyond the last page | 200 | Empty `data` with correct totals |

Errors use the standard `ApiResponse` failure shape. `GlobalExceptionHandler` produces them by mapping an `ArgumentException` to 400.

---

## How it is implemented

### Request flow

```
GET /api/{Entity}/get?Page=2&PageSize=10&Filters=...&Sorts=...
        │
        ▼
GetRequestModelBinder        reads Filters, Sorts, Page, PageSize → GetRequest
        │                    (invalid Page/PageSize → ArgumentException → 400)
        ▼
BaseController.Get           calls _service.GetAsync(request)
        │
        ▼
BaseBusinessLayer.GetAsync
   1. Filter(...)            WHERE  (existing)
   2. Sort(...)              ORDER BY (existing)
   3. not paged?  → ToListAsync(), return all rows
   4. CountAsync()           total rows after filtering
   5. no sort?    → order by primary key (stable pages)
   6. Paginate(page, size)   OFFSET / FETCH (Skip/Take)
   7. ToListAsync()
        │
        ▼
GetResponse { Data, Pagination }
        │
        ▼
ApiResponse.SuccessResult(data, message, pagination)
```

The database runs two queries per paged request: one `COUNT(*)`, and one `SELECT … ORDER BY … OFFSET … FETCH`.

### Files changed

| File | Change |
|---|---|
| `i25Core.Core/DTO/PaginationMeta.cs` | **New.** Paging metadata returned to the client |
| `i25Core.Core/DTO/ApiResponse.cs` | Added `Pagination` property and optional `pagination` argument on `SuccessResult` |
| `i25Core.BusinessLayer/Parameters/GetRequest.cs` | Added `Page`, `PageSize`, defaults, cap, and helper properties |
| `i25Core.BusinessLayer/Parameters/GetResponse.cs` | Added `Pagination` property |
| `i25Core.BusinessLayer/Extensions/QueryableExtensions.cs` | Added `Paginate()` extension (Skip/Take) |
| `i25Core.BusinessLayer/Implementation/Base/BaseBusinessLayer.cs` | `GetAsync` counts, orders and pages; added `DefaultSort()` |
| `i25Core.Web/Helpers/GetRequestModelBinder.cs` | Reads and validates `Page` / `PageSize` |
| `i25Core.Web/Controllers/BaseController.cs` | Passes `Pagination` into the response |

### 1. `PaginationMeta` (Core)

```csharp
public class PaginationMeta
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PaginationMeta Create(int page, int pageSize, int totalCount) => new()
    {
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
    };
}
```

It lives in `Core/DTO` next to `ApiResponse`, so both the business layer and the web layer can use it.

### 2. `GetRequest` (BusinessLayer)

```csharp
public const int DefaultPageSize = 20;
public const int MaxPageSize = 100;

public int? Page { get; set; }
public int? PageSize { get; set; }

public bool IsPaged => Page.HasValue || PageSize.HasValue;
public int ResolvedPage => Math.Max(Page ?? 1, 1);
public int ResolvedPageSize => Math.Clamp(PageSize ?? DefaultPageSize, 1, MaxPageSize);
```

- **Nullable properties:** they let the code tell "not sent" apart from a real value, which is what makes paging opt-in.
- **Default and cap:** `ResolvedPage` and `ResolvedPageSize` apply them in one place.
- **Changing the values:** to use a different default or cap, change `DefaultPageSize` or `MaxPageSize`.

### 3. `GetRequestModelBinder` (Web)

```csharp
request.Page = ReadPositiveInt(bindingContext, "Page");
request.PageSize = ReadPositiveInt(bindingContext, "PageSize");

private static int? ReadPositiveInt(ModelBindingContext ctx, string name)
{
    var raw = ctx.ValueProvider.GetValue(name).FirstValue;
    if (string.IsNullOrWhiteSpace(raw)) return null;

    if (!int.TryParse(raw, out var value) || value < 1)
        throw new ArgumentException($"{name} must be a whole number greater than 0.");

    return value;
}
```

The binder throws instead of correcting the value silently, so clients find out when they send bad input. `GlobalExceptionHandler` turns the `ArgumentException` into a 400 `ApiResponse`.

### 4. `Paginate()` (QueryableExtensions)

```csharp
public static IQueryable<T> Paginate<T>(this IQueryable<T> query, int page, int pageSize)
    => query.Skip((page - 1) * pageSize).Take(pageSize);
```

EF Core translates this into SQL Server `OFFSET … ROWS FETCH NEXT … ROWS ONLY`, so the database does the paging rather than the application.

### 5. `BaseBusinessLayer.GetAsync`

```csharp
if (request?.IsPaged != true)
    return new GetResponse { Success = true, Message = "...", Data = await query.ToListAsync() };

var page = request.ResolvedPage;
var pageSize = request.ResolvedPageSize;

var totalCount = await query.CountAsync();          // after Filter, before Skip/Take

if (request.Sorts?.Any() != true)
    query = query.Sort(DefaultSort());              // primary-key order

var data = await query.Paginate(page, pageSize).ToListAsync();

return new GetResponse
{
    Success = true,
    Message = "Records retrieved successfully.",
    Data = data,
    Pagination = PaginationMeta.Create(page, pageSize, totalCount)
};
```

`DefaultSort()` reads the entity's primary key from EF metadata. Because it doesn't assume a column called `id`, it also works for keys like `AuditLogId`:

```csharp
private IEnumerable<GetSort> DefaultSort()
{
    var keyProperties = _context.Model.FindEntityType(typeof(T))?
        .FindPrimaryKey()?
        .Properties;

    return keyProperties?.Select(p => new GetSort { Column = p.Name })
        ?? Enumerable.Empty<GetSort>();
}
```

### 6. `BaseController.Get`

```csharp
return Ok(ApiResponse<object?>.SuccessResult(
    response.Data,
    response.Message,
    response.Pagination));
```

---

## Design decisions

| Decision | Reason |
|---|---|
| **Opt-in paging** | Existing clients that call `/get` without parameters keep receiving every row. Nothing breaks. |
| **Count after filtering** | `totalCount` reflects what the user is actually browsing, not the whole table. |
| **Primary-key fallback order** | `Skip`/`Take` on an unordered query has no guaranteed order in SQL Server, so pages could repeat or skip rows. |
| **Cap at 100** | Protects the API and the database from `PageSize=1000000`. |
| **Reject `Page=0`, don't clamp it** | A bad value is a client bug, so the client should hear about it. |
| **Page past the end returns 200 + empty list** | It isn't an error, and the client can read `totalPages` to recover. |
| **Metadata in `ApiResponse.Pagination`** | Keeps every response in one envelope instead of a separate paged response type. |

---

## Custom business layers

A business layer that overrides `GetAsync`, for example to add `.Include(...)`, can reuse the same pieces:

```csharp
public override async Task<GetResponse> GetAsync(GetRequest request)
{
    IQueryable<Order> query = _dbSet.Include(o => o.Lines);

    query = query.Filter(request.Filters).Sort(request.Sorts);

    if (!request.IsPaged)
        return new GetResponse { Success = true, Data = await query.ToListAsync() };

    var total = await query.CountAsync();

    if (!request.Sorts.Any())
        query = query.OrderBy(o => o.id);

    var data = await query
        .Paginate(request.ResolvedPage, request.ResolvedPageSize)
        .ToListAsync();

    return new GetResponse
    {
        Success = true,
        Data = data,
        Pagination = PaginationMeta.Create(request.ResolvedPage, request.ResolvedPageSize, total)
    };
}
```

---

## Testing performed

These were run against the API on a local SQL Server, with 25 audit log rows.

| Request | Result |
|---|---|
| No paging parameters | All 25 rows, `pagination: null` |
| `Page=1&PageSize=10` | Rows 1–10, `totalPages: 3`, `hasNext: true` |
| `Page=3&PageSize=10` | Rows 21–25, `hasNext: false` |
| `Page=2` (default size) | Rows 21–25, `pageSize: 20` |
| Filter + sort desc + `PageSize=5` | Rows 25–21 |
| `Page=99` | Empty `data`, `totalCount: 25` |
| `PageSize=500` | Capped to `pageSize: 100` |
| `Page=0` | 400 |
| `PageSize=abc` | 400 |

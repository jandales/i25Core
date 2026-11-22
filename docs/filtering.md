# Filtering

The `/get` endpoint on every controller that inherits `BaseController<T>` accepts a `Filters` list. Each filter becomes part of a SQL `WHERE` clause that EF Core generates. Nothing is filtered in memory.

---

## Usage

### Request

```http
GET /api/{Entity}/get?Filters=[{"column":"EntityId","value":"10","condition":">"}]
```
URL-encode the `Filters` value in real requests.

Each filter has four fields:

| Field | Required | Description |
|---|---|---|
| `column` | yes | Property name on the entity. Matching ignores case (`entityType` matches `EntityType`). |
| `condition` | no | Operator, see the table below. Defaults to `=` |
| `value` | depends | Value as a string. Lists use commas: `"1,2,3"` |
| `conjunction` | no | `AND` (default) or `OR`. It joins this filter to the **next** one. |

### Operators

Operator names ignore case, spaces, `_` and `-`, so `notContains`, `not contains`, `NOT_CONTAINS` and `not-contains` all work.

| Operator | Aliases | Column types | `value` | SQL |
|---|---|---|---|---|
| `=` | `eq`, `equal`, `equals` | any | single value, or `null` | `col = @v` / `col IS NULL` |
| `!=` | `<>`, `ne`, `notEqual`, `notEquals` | any | single value, or `null` | `col <> @v` / `col IS NOT NULL` |
| `>` | `gt`, `greaterThan` | number, date, text | single value | `col > @v` |
| `>=` | `gte`, `greaterThanOrEqual` | number, date, text | single value | `col >= @v` |
| `<` | `lt`, `lessThan` | number, date, text | single value | `col < @v` |
| `<=` | `lte`, `lessThanOrEqual` | number, date, text | single value | `col <= @v` |
| `between` | — | number, date, text | `"from,to"` (inclusive) | `col >= @from AND col <= @to` |
| `contains` | `like` | text | single value | `col LIKE '%v%'` |
| `notContains` | `notLike` | text | single value | `col IS NULL OR col NOT LIKE '%v%'` |
| `startsWith` | — | text | single value | `col LIKE 'v%'` |
| `endsWith` | — | text | single value | `col LIKE '%v'` |
| `in` | — | any | `"a,b,c"` | `col IN (@a, @b, @c)` |
| `notIn` | — | any | `"a,b,c"` | `col NOT IN (...)` |
| `isNull` | — | nullable only | not used | `col IS NULL` |
| `isNotNull` | — | nullable only | not used | `col IS NOT NULL` |

Text comparisons follow the database collation. SQL Server's default collation ignores case, so `contains "order"` also matches `"ORDER moved"`.

### Value formats

| Column type | Example values |
|---|---|
| Text | `"Order"` |
| Whole number / decimal | `"10"`, `"19.95"` (always use `.` as the decimal separator) |
| Date / DateTime | `"2026-04-01"`, `"2026-04-01T08:30:00"` (ISO 8601) |
| Boolean | `"true"`, `"false"`, `"1"`, `"0"` |
| Guid | `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"` |
| Enum | name (`"Active"`, case-insensitive) or number (`"1"`) |

### Examples

**Changes made between two dates**
```json
[{ "column": "ChangedAt", "condition": "between", "value": "2026-02-01,2026-04-30" }]
```

**Several specific ids**
```json
[{ "column": "EntityId", "condition": "in", "value": "1,3,5" }]
```

**Text search**
```json
[{ "column": "Remarks", "condition": "contains", "value": "price" }]
```

**Rows with no name**
```json
[{ "column": "ChangedByName", "condition": "isNull" }]
```

**Combining with AND / OR**
```json
[
  { "column": "EntityType", "condition": "=", "value": "Order", "conjunction": "AND" },
  { "column": "ChangedBy",  "condition": "=", "value": "2" }
]
```
`conjunction` sits on the **first** filter and joins it to the next. Filters are combined left to right, so `A OR B AND C` is evaluated as `(A OR B) AND C`.

**With sorting and paging**
```http
GET /api/AuditLogContoller/get
    ?Filters=[{"column":"EntityType","condition":"in","value":"Order,Invoice"}]
    &Sorts=[{"column":"ChangedAt","direction":"desc"}]
    &Page=1&PageSize=20
```

### Errors

Invalid filters return **400** in the standard `ApiResponse` failure shape. They never return a 500.

| Cause | Message |
|---|---|
| Unknown column | `Invalid filter column: Foo` |
| Unknown operator | `Unsupported condition: approx` |
| Text operator on a non-text column | `Condition 'contains' can only be used on text columns; EntityId is whole number.` |
| Value doesn't fit the column type | `Value 'abc' is not a valid whole number for column EntityId.` |
| `between` without exactly two values | `Condition 'between' on column EntityId needs two values separated by a comma, e.g. "10,20".` |
| `in` with no values | `Condition 'in' on column EntityId needs at least one value, e.g. "1,2,3".` |
| `isNull` on a non-nullable column | `Column EntityId cannot be null, so 'isNull' / 'isNotNull' do not apply.` |
| Comparison operator with no value | `A value is required to filter column EntityId.` |
| `>` / `<` on a boolean or Guid column | `Condition 'GreaterThan' is not supported for column IsActive.` |

---

## How it is implemented

### Files changed

| File | Change |
|---|---|
| `i25Core.BusinessLayer/Extensions/FilterExpressionBuilder.cs` | **New.** Builds the expression for one filter (all operators, value conversion, errors) |
| `i25Core.BusinessLayer/Extensions/QueryableExtensions.cs` | `Filter()` now calls `FilterExpressionBuilder.Build(...)` instead of its own `=` / `!=` switch |

The request model (`GetFilter`), the model binder and the controllers are unchanged, so existing clients keep working.

### Flow

```
Filters JSON ─► GetRequestModelBinder ─► List<GetFilter>
                                              │
QueryableExtensions.Filter<T>()               ▼
   for each filter:
     1. find the property on T (case-insensitive)   → 400 if missing
     2. FilterExpressionBuilder.Build(property, filter)
          a. Normalize(condition)   "Not Contains" → "notcontains"
          b. pick the operator
          c. ConvertValue(...)      "2026-04-01" → DateTime  → 400 if invalid
          d. return an Expression   e.g. x.ChangedAt > 2026-04-01
     3. join with the previous expression using AND / OR
   query.Where(x => <combined expression>)
                                              │
EF Core ─► SQL WHERE clause ──────────────────┘
```

### Key techniques

**Expression trees, not string SQL.** Every operator builds a LINQ `Expression`, which EF Core translates into parameterized SQL. User input never becomes SQL text, so it can't cause SQL injection.

**Constants are typed as the property type.** Values are converted and wrapped as `Expression.Constant(value, property.Type)`, including `Nullable<T>`. Comparisons on nullable columns such as `DateTime?` therefore work without casting the column, and SQL `NULL` rules behave correctly.

**Text operators guard against null.**
```csharp
// contains → x.Col != null && x.Col.Contains(value)
Expression.AndAlso(
    Expression.NotEqual(property, Expression.Constant(null, typeof(string))),
    Expression.Call(property, StringContains, Expression.Constant(value)));
```
EF translates `string.Contains`, `StartsWith` and `EndsWith` into `LIKE`. `notContains` is `Not(contains)`, so rows where the column is `NULL` are **included**.

**Text `>` / `<` use `string.Compare`.** C# has no `>` operator for strings, so the builder generates `string.Compare(x.Col, value) > 0`. EF Core translates that into a plain SQL `col > @v`.

**`in` uses `List<T>.Contains`.**
```csharp
// new List<int> { 1, 3, 5 }.Contains(x.EntityId)  →  EntityId IN (1, 3, 5)
```
The list is created with the property's own type, so `in` works for numbers, text, dates, enums and Guids.

**`between` reuses `>=` and `<=`.** It splits `"from,to"` and joins two comparisons with `AND`. Both ends are included.

**One place for value conversion.** `ConvertValue()` handles text, numbers, dates (`DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`), booleans (`true`/`false`/`1`/`0`), Guids and enums, always with `InvariantCulture`. A failed conversion becomes an `ArgumentException` with a readable message. `GlobalExceptionHandler` maps it to 400.

### Adding an operator

1. Add a case to the `switch` in `FilterExpressionBuilder.Build`, with its aliases written in lowercase and without separators.
2. Return an `Expression` that EF Core can translate. If EF can't translate it, the request fails at runtime with "could not be translated".
3. Throw `ArgumentException` for invalid input, so the client gets a 400.
4. Add the operator to the tables in this document.

---

## Design decisions

| Decision | Reason |
|---|---|
| **Keep the `GetFilter` shape** (`column`, `value`, `condition`, `conjunction`) | No breaking change for existing clients. `=`, `Equal`, `!=` and `NotEqual` still work. |
| **Separate `FilterExpressionBuilder` class** | Keeps `Filter()` focused on combining filters, and puts each operator's logic in one place |
| **Forgiving operator names** (case, spaces, `_`, `-`, aliases) | Frontends can send `gte`, `>=` or `greaterThanOrEqual` without the API being strict about spelling |
| **Comma-separated lists** for `in` and `between` | `value` stays a plain string, matching the existing contract. Note that individual list values can't contain commas. |
| **`= null` means `IS NULL`** | It matches what people expect, rather than failing on a null value |
| **400 for every invalid filter** | Bad input is a client error. Before this change, a bad value such as `"abc"` for a number column caused a 500. |
| **Text comparisons follow the collation** | Case-insensitive search comes from the database and keeps indexes usable. Lower-casing in code would bypass them. |

---

## Testing performed

These were run against the API on a local SQL Server, with 6 audit log rows of mixed text, number, date and null values. 23 query cases and 6 error cases passed.

| Case | Result |
|---|---|
| `=` / `!=` on text | ✓ |
| `>` `>=` `<` `<=` on int | ✓ |
| `>` on date | ✓ |
| `>` on text (`string.Compare`) | ✓ |
| `between` on int and on date | ✓ |
| `contains` (including case-insensitive match), `notContains` (keeps null rows) | ✓ |
| `startsWith`, `endsWith` (`"ends with"` spelling) | ✓ |
| `in` on int and on text (with spaces), `notIn` | ✓ |
| `isNull`, `isNotNull` (`"is_not_null"` spelling), `= null` | ✓ |
| `AND` and `OR` combinations | ✓ |
| Errors: text operator on int, bad int value, `between` with one value, `isNull` on a non-null column, unknown operator, missing value | all 400 with the messages above |

No client-side evaluation or "could not be translated" warnings appeared in the logs.

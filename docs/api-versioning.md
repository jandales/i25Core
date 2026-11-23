# API Versioning

Every endpoint can be reached with a version in the URL: `/api/v1/Vendor`. When you later need to change an endpoint in a way that would break existing clients, you publish `/api/v2/Vendor` next to it, and old clients keep using v1 until they move.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Versioned URLs | `/api/v1/...`, `/api/v2/...` | `Route::prefix('v1')` |
| Old URLs keep working | `/api/Vendor` means the default version | An unprefixed legacy route |
| New behaviour without breaking old clients | A second controller class marked `[ApiVersion("2.0")]` | A `V2` controller namespace |
| Tell clients a version is going away | `[ApiVersion("1.0", Deprecated = true)]` | Custom deprecation headers |
| Docs per version | One Swagger document per version | Per-version docs |

Built on Microsoft's `Asp.Versioning.Mvc` packages (8.1.0).

---

## What you get without writing any code

Every controller that inherits `BaseController<T>` (and `NotificationController`) answers on **both** URLs:

```
GET /api/Vendor        ← unversioned: the default version (1.0), exactly as before
GET /api/v1/Vendor     ← explicit version 1
```

Existing clients and URLs keep working. A controller with no `[ApiVersion]` attribute is **version 1.0**.

Responses carry version headers:

```
api-supported-versions: 2.0
api-deprecated-versions: 1.0
```

Swagger (in Development) shows a version drop-down (V2, V1) and lists only the `/api/v{n}/...` URLs.

---

## Configuration

`appsettings.json`:

```json
"ApiVersioning": {
  "DefaultVersion": "1.0",
  "AssumeDefaultVersionWhenUnspecified": true,
  "ReadFromHeader": true,
  "HeaderName": "X-Api-Version",
  "ReportApiVersions": true
}
```

| Setting | Default | Description |
|---|---|---|
| `DefaultVersion` | `1.0` | The version unversioned URLs get, **and the version of every controller without `[ApiVersion]`**. See the warning below. |
| `AssumeDefaultVersionWhenUnspecified` | `true` | `true`: `/api/Vendor` works. `false`: every call must name a version; unversioned calls get a 400 ("An API version is required, but was not specified."). |
| `ReadFromHeader` | `true` | Also accept the version in a header, e.g. `X-Api-Version: 2.0` |
| `HeaderName` | `X-Api-Version` | Name of that header |
| `ReportApiVersions` | `true` | Send `api-supported-versions` / `api-deprecated-versions` on responses |

An invalid `DefaultVersion` (such as `abc`) stops the app at startup.

> **Warning: changing `DefaultVersion` moves your existing controllers.** A controller with no `[ApiVersion]` has no version of its own, so it takes the default. Set `DefaultVersion` to `2.0` and every un-annotated controller becomes **2.0**, and `/api/v1/...` for them stops working (404). Before moving the default, add an explicit `[ApiVersion("1.0")]` to each controller that must stay on v1.

---

## How a request picks a version

| Request | Version used |
|---|---|
| `GET /api/v2/Vendor` | 2.0 (from the URL) |
| `GET /api/Vendor` | the default (1.0) |
| `GET /api/Vendor` with header `X-Api-Version: 2.0` | 2.0 (from the header) |
| `GET /api/v1/Vendor` with header `X-Api-Version: 2.0` | **Error 400**: two different versions were requested |

---

## Adding a new version

### A. A second controller class (the usual way)

Keep the existing controller as it is and add a new class for v2. Both use the same URL; the version chooses between them.

```csharp
using Asp.Versioning;

// Existing controller. Mark it explicitly once you start versioning.
[ApiVersion("1.0")]
public class VendorController : BaseController<Vendor> { ... }

// New version
[ApiVersion("2.0")]
[ControllerName("Vendor")]               // keeps the URL /api/v2/Vendor (not /api/v2/VendorV2)
public class VendorV2Controller : BaseController<Vendor>
{
    public VendorV2Controller(IBusinessLayer<Vendor> service) : base(service) { }

    [HttpGet("summary")]                 // /api/v2/Vendor/summary: only exists in v2
    public IActionResult Summary() => Ok(...);
}
```

- `[ControllerName("Vendor")]` is required, otherwise the class name `VendorV2` becomes part of the URL.
- The route attributes on `BaseController` are inherited, so the new class needs no `[Route]`.
- A v2 class that inherits `BaseController<T>` also serves all the standard endpoints (list, get by id, create, update, delete) under `/api/v2/...`.

### B. One class that serves several versions

Useful while v2 is "v1 plus one extra endpoint":

```csharp
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class ReportController : BaseController<Report>
{
    [HttpGet("export")]
    [MapToApiVersion("2.0")]             // this action exists only in v2
    public IActionResult Export() => Ok(...);
}
```

### C. Version-neutral endpoints

Things that are the same in every version (a health check, a webhook) are marked neutral and ignore versions:

```csharp
[ApiVersionNeutral]
[Route("api/health")]
public class HealthController : ControllerBase { ... }
```

For your own controllers that don't inherit `BaseController`, add both routes to get the same dual URLs:

```csharp
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
```

---

## Deprecating and retiring a version

**1. Mark it deprecated.** Clients keep working but are told:

```csharp
[ApiVersion("1.0", Deprecated = true)]
public class VendorController : BaseController<Vendor> { ... }
```

- Responses get `api-deprecated-versions: 1.0`.
- Swagger marks that version's operations as deprecated (strikethrough).

**2. Announce it** to your API users, with a date.

**3. Retire it** by deleting the old controller class. After that, calls to `/api/v1/Vendor` return 404.

---

## What clients see when something is wrong

Version errors use the API's normal `ApiResponse` envelope, with the HTTP status and the version headers:

```json
{
  "success": false,
  "message": "The HTTP resource that matches the request URI 'http://host/api/Vendor' does not support the API version '9.0'.",
  "data": null,
  "errors": null,
  "traceId": "00-...",
  "pagination": null
}
```

| Situation | Result |
|---|---|
| Version in the **URL** that no controller supports (`/api/v3/Vendor`, `/api/v1.5/Vendor`) | **404**, empty body. With the version in the URL path, that URL simply doesn't exist, the same as any unknown route. |
| Version in a **header** that no controller supports (`X-Api-Version: 9.0`) | **400** envelope |
| Malformed version (`/api/vabc/Vendor`, `X-Api-Version: banana`) | **400** envelope |
| URL and header disagree | **400** envelope ("At most, only a single API version may be specified") |
| No version, and `AssumeDefaultVersionWhenUnspecified` is `false` | **400** envelope ("An API version is required...") |
| An action that exists only in another version (`/api/v1/Vendor/summary`) | **404** |

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.AspNetCore/Versioning/VersioningServiceExtensions.cs` | **New.** `AddI25ApiVersioning` (configures the library) and `UseI25SwaggerUI` |
| `i25Core.AspNetCore/Versioning/ApiVersioningSettings.cs` | **New.** `ApiVersioning` settings |
| `i25Core.AspNetCore/Versioning/ApiVersionProblemWriter.cs` | **New.** Turns version errors into the `ApiResponse` envelope |
| `i25Core.AspNetCore/Versioning/SwaggerVersioning.cs` | **New.** One Swagger document per version; marks deprecated operations |
| `i25Core.AspNetCore/Controllers/BaseController.cs` | Second `[Route("api/v{version:apiVersion}/[controller]")]` |
| `i25Core.AspNetCore/Controllers/NotificationController.cs` | Same second route |
| `Program.cs`, `appsettings.json`, `i25Core.Web.csproj` | Registration, settings, `Asp.Versioning.Mvc` and `.ApiExplorer` 8.1.0 |

No database changes.

### How routing works

```
GET /api/v2/Vendor
        │
        ▼  two route templates exist for each controller
   "api/Vendor"                          ← unversioned alias
   "api/v{version:apiVersion}/Vendor"    ← matches; version = "2"
        │
        ▼  Asp.Versioning: several controllers share this URL, one per version
   VendorController    [1.0]
   VendorV2Controller  [2.0]   ← chosen
```

For `/api/Vendor` the version is missing, so the default (1.0) is assumed and `VendorController` is chosen.

### Design decisions

| Decision | Reason |
|---|---|
| **Version in the URL (`/api/v1/...`)** | Easy to read, test in a browser and cache; the Laravel-style prefix you asked for |
| **Keep the unversioned alias** | Existing clients and bookmarks don't break. Set `AssumeDefaultVersionWhenUnspecified` to `false` if you want to force explicit versions. |
| **Header as a second way to choose** | Handy for clients that prefer not to change URLs. Can be switched off. |
| **A new controller class per version** | Each version is plain, readable code, and removing a version means deleting a class |
| **Version routes live in `BaseController`** | All current and future entity controllers are versioned automatically |
| **No `[ApiVersion]` on `BaseController`** | The attribute would be inherited by every v2 subclass, which would then claim v1 too and collide with the real v1 controller |
| **Version errors in the `ApiResponse` envelope** | One error shape for the whole API. By default the library answers in a different format (RFC 7807). |
| **One Swagger document per version, versioned URLs only** | Otherwise every operation would appear twice in every document (once with the unversioned alias) |
| **Fail at startup for a bad `DefaultVersion`** | A typo is found when you deploy, not by the first request |

---

## Limitations

| Limitation | Notes |
|---|---|
| Un-annotated controllers take `DefaultVersion` | Add explicit `[ApiVersion("1.0")]` before changing the default (see the warning above) |
| `BaseController` actions are not `virtual` | A v2 class can **add** endpoints but cannot override an inherited one. To change how an existing endpoint behaves in v2, write that controller as a new class deriving from `ControllerBase`. |
| An unsupported version in the URL is a 404, not a 400 | This is how the library treats URL-path versions. Header versions get the 400 and a helpful message. |
| Version-neutral endpoints reject an *unknown* version sent in a header | `/api/health` with `X-Api-Version: 9.0` returns 400. A real version (`2.0`) or none works. |
| No `Sunset` (retirement date) header | The library supports it (`options.Policies.Sunset(...)`), but no date is configured because none is known yet |
| The version is not read from the query string (`?api-version=2.0`) or the content type | Only the URL and, optionally, one header |
| The unversioned alias is hidden from Swagger | It still works; it just isn't listed |

---

## Testing performed

These were run against the API on a temporary SQL Server database with temporary test controllers (removed afterwards): a `Probe` with a deprecated v1 and a v2, a controller serving both versions, a version-neutral controller, and a v2 of a real entity controller (`BaseController<AuditLog>`). About 56 checks.

| Area | Result |
|---|---|
| Existing endpoints at `/api/X` and `/api/v1/X` (including `NotificationController`) | Both work; identical responses; a normal 404 keeps its own message |
| Choosing a version: URL, unversioned, header, URL and header together | v1 / v2 / default v1 / v2 from header / 400 when they disagree |
| Deprecated version | `api-deprecated-versions: 1.0` and `api-supported-versions: 2.0` on responses, including error responses |
| Bad versions | Header 9.0 → 400 envelope; `banana` → 400; `/api/vabc/...` → 400; `/api/v3/...` and `/api/v1.5/...` → 404 |
| Error shape | `success:false`, message, `traceId`, JSON content type, no RFC 7807 fields |
| A class serving 1.0 and 2.0, with a 2.0-only action | Works in 2.0; 404 in 1.0 |
| Version-neutral controller | Works with no version and with a real header version |
| A v2 class inheriting `BaseController<AuditLog>` | Inherited list works at `/api/v2/...`; its own `v2only` action works |
| Swagger | `v1` and `v2` documents exist, `v3` is 404; only `/api/v{n}/...` paths; version substituted in URLs; UI lists V2 then V1 |
| Swagger deprecation | Deprecated version's operations flagged (2 of 2), none in the v2 document |
| `AssumeDefaultVersionWhenUnspecified = false` | Unversioned call → 400 "An API version is required"; versioned and header calls still work |
| `ReadFromHeader = false` / custom `HeaderName` | Header ignored / only the new header name read |
| `ReportApiVersions = false` | No version headers |
| `DefaultVersion = 2.0` | Confirmed the warning: un-annotated controllers move to 2.0 and `/api/v1/...` returns 404 for them |
| `DefaultVersion = abc` | Startup stops with a clear message |

### Defects found and fixed during testing

1. **Version errors came back in the library's RFC 7807 format**, not the `ApiResponse` envelope. My error writer looked for a `code` field the library doesn't send. It now recognises the errors by their `type`.
2. **Swagger didn't mark a deprecated version's operations as deprecated**, because Swashbuckle only reads `[Obsolete]`. A small operation filter now does it.

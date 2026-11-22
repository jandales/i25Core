# File Storage

Code that saves or reads files talks to one interface, `IFileStorage`. Where the files actually live (a folder on the server today, S3 or Azure later) is a configuration choice, so changing it doesn't touch your application code.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Save, read, delete, copy, move, list files | `IFileStorage` | `Storage::put / get / delete / copy / move / files` |
| Several storage places (private, public, ...) | Named disks in `appsettings.json`, picked with `IFileStorageManager.Disk("public")` | `Storage::disk('public')` |
| A browser-reachable URL for a file | `disk.GetUrl(path)` and the public disk served at `/storage` | `Storage::url()` + `storage:link` |
| Handle an uploaded file safely | `IFileUploader.StoreAsync(file)` | `$request->file('x')->store('dir')` |
| Move to the cloud later | Register another driver; no app code changes | `config/filesystems.php` drivers |

**What is included:** the `Local` driver (folders on the server's disk). **Not included:** S3 or Azure drivers. The extension point exists (see [Adding a driver](#adding-a-driver-s3-azure)), but I did not ship cloud code that could not be tested here.

---

## Configuration

`appsettings.json`:

```json
"Storage": {
  "Default": "local",
  "Disks": {
    "local":  { "Driver": "Local", "Root": "storage/app" },
    "public": { "Driver": "Local", "Root": "storage/public", "Url": "/storage" }
  },
  "Uploads": {
    "MaxBytes": 10485760,
    "AllowedExtensions": [ ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".txt", ".csv", ".docx", ".xlsx" ],
    "Disk": "public"
  }
}
```

### Disks

| Setting | Description |
|---|---|
| `Default` | The disk you get when you inject `IFileStorage` |
| `Disks:{name}:Driver` | `Local` (built in), or the name of a driver you registered |
| `Disks:{name}:Root` | Local driver: the folder holding the files. Relative paths start at the app's root folder; absolute paths work too. The folder is created if it doesn't exist. |
| `Disks:{name}:Url` | Makes the disk public. A path such as `/storage` is served by this app. A full address such as `https://cdn.example.com/files` is used as is (something else serves the files). Leave it out for a private disk. |
| `Disks:{name}:Settings` | Free-form values for custom drivers (`Bucket`, `Region`, ...) |

Two disks are used by default:

| Disk | Folder | Reachable from a browser? | Use it for |
|---|---|---|---|
| `local` | `storage/app` | **No** | Invoices, exports, anything private |
| `public` | `storage/public` | **Yes**, at `/storage/...` | Product images, avatars, public downloads |

If `Disks` is left out entirely, those two defaults are used. If you configure `Disks`, your list **replaces** the defaults (a disk you remove is really gone).

### Upload rules (`Uploads`)

| Setting | Default | Description |
|---|---|---|
| `MaxBytes` | `10485760` (10 MB) | Largest accepted upload |
| `AllowedExtensions` | the ten shown above | Your list **replaces** the defaults; it is not added to them. Use `["*"]` to accept any extension (not recommended, see [Security](#security)). |
| `Disk` | `Default` disk | Where `IFileUploader` stores files when no disk is named |

The runtime folders (`storage/...`) are listed in `.gitignore`, so uploaded files are never committed.

> **Web server limits:** ASP.NET Core also limits request size (about 30 MB by default). `MaxBytes` should stay below that.

---

## Using it

### Save and read files

Inject `IFileStorage` (the default disk) or `IFileStorageManager` (any disk):

```csharp
using i25Core.Core.Storage;

public class InvoiceExporter
{
    private readonly IFileStorage _files;                 // the default disk
    private readonly IFileStorageManager _disks;

    public InvoiceExporter(IFileStorage files, IFileStorageManager disks) { _files = files; _disks = disks; }

    public async Task Run(int invoiceId, byte[] pdf)
    {
        await _files.PutAsync($"invoices/2026/{invoiceId}.pdf", pdf);            // private disk
        await _disks.Disk("public").PutTextAsync("status/last-run.txt", "ok");    // public disk

        if (await _files.ExistsAsync($"invoices/2026/{invoiceId}.pdf")) { /* ... */ }

        string text = await _files.GetTextAsync("notes/readme.txt");
        byte[] bytes = await _files.GetBytesAsync($"invoices/2026/{invoiceId}.pdf");

        await using Stream stream = await _files.OpenReadAsync("big/export.csv");  // streams; you dispose it
    }
}
```

### All operations

| Method | What it does | Failure |
|---|---|---|
| `PutAsync(path, stream/bytes, overwrite = true)` | Writes a file, creating folders as needed | `overwrite: false` on an existing file → **409** |
| `PutTextAsync(path, text)` | Writes UTF-8 text | |
| `OpenReadAsync(path)` | Opens a read stream (dispose it) | Missing file → **404** |
| `GetBytesAsync` / `GetTextAsync` | Reads a whole file | Missing file → **404** |
| `ExistsAsync(path)` | `true` / `false` | |
| `GetInfoAsync(path)` | Size, modified time (UTC), content type, URL; or `null` if missing | |
| `DeleteAsync(path)` | `true` if deleted, `false` if it wasn't there | |
| `DeleteDirectoryAsync(dir)` | Deletes a folder and everything in it | Deleting the disk root → **400** |
| `CopyAsync(from, to, overwrite = false)` | Copies a file | Missing source → **404**, existing target → **409** |
| `MoveAsync(from, to, overwrite = false)` | Moves or renames a file | same |
| `ListAsync(dir = "", recursive = false)` | Files in a folder, sorted by path. A missing folder gives an empty list. | |
| `GetUrl(path)` | Public URL, or `null` on a private disk. It does not check the file exists. | |

Paths are relative to the disk and written with `/`: `invoices/2026/1.pdf`. Backslashes are accepted and converted.

### Accept an upload

```csharp
[ApiController, Route("api/[controller]")]
public class AvatarController : ControllerBase
{
    private readonly IFileUploader _uploader;
    public AvatarController(IFileUploader uploader) => _uploader = uploader;

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        UploadResult saved = await _uploader.StoreAsync(file, directory: "avatars");
        return Ok(ApiResponse<UploadResult>.SuccessResult(saved));
    }
}
```

```json
{
  "disk": "public",
  "path": "avatars/e65a6d00819e4b5d8c652dc7db969d6c.png",
  "url": "/storage/avatars/e65a6d00819e4b5d8c652dc7db969d6c.png",
  "size": 48213,
  "contentType": "image/png",
  "originalName": "me.png"
}
```

Store `path` (and the disk) in your database; show `url` to browsers.

`StoreAsync(file, directory, disk)`:

| Check | If it fails (HTTP 400) |
|---|---|
| File is not empty | "The uploaded file is empty." |
| Size ≤ `MaxBytes` | "The file is too large (2 MB). The limit is 1 MB." |
| Extension is allowed (case-insensitive; `x.JPG` is fine) | "Files of type '.exe' are not allowed. Allowed: ..." |
| `directory` is a valid path | "'../../x' is not a valid storage path ..." |

The stored name is always a **random name** (`{guid}.png`). The user's file name is only returned as `originalName`, for display.

### Reading files back for a download

Private files are not reachable from a browser. Serve them from your own endpoint, after your own checks:

```csharp
[HttpGet("invoices/{id}")]
public async Task<IActionResult> Invoice(int id)
{
    // check here that the signed-in user may see invoice {id}
    var info = await _files.GetInfoAsync($"invoices/{id}.pdf");
    if (info == null) return NotFound();

    return File(await _files.OpenReadAsync(info.Path), info.ContentType, $"invoice-{id}.pdf");
}
```

---

## Security

File handling is a common source of vulnerabilities, so these protections are built in:

| Risk | Protection |
|---|---|
| **Path traversal** (`../../secret`, `C:\Windows\x`) | Every path goes through `StoragePath`: `.` and `..` segments, drive letters and invalid characters are rejected with 400. After joining with the disk root, the result is checked again to be inside the root. Tested with 22 attack patterns. |
| **Windows tricks** | Reserved names (`CON`, `NUL`, `COM1`...), names ending in a dot or space and over-long names are rejected |
| **User chooses where a file goes or overwrites others** | `IFileUploader` ignores the user's name and generates a random one; it also never overwrites |
| **Dangerous file types** | An extension allow-list (the default is images, PDF, text, CSV, Office) |
| **Fake content type** | The content type is taken from the checked extension, never from the browser's `Content-Type` header |
| **Stored XSS through an uploaded `.html` or `.svg`** | The public disk is served with `X-Content-Type-Options: nosniff` and `Content-Security-Policy: sandbox`, so scripts in an opened file don't run on your domain. The default allow-list also excludes `.html` and `.svg`. |
| **Hidden and unknown files exposed** | Dot-files (`.env`) and files with unknown extensions are never served |
| **Private files exposed** | Only disks with a path `Url` are served. The `local` disk is never reachable over HTTP. |
| **Disk root exposed** | A disk `Url` of `/` or under `/api` stops the app at startup |
| **Reading half-written files** | Writes go to a temporary file and are moved into place in one step |

**What is not done:**
- **File content is not inspected.** A file named `x.png` is trusted to be a PNG. For sensitive apps add content checks (image decoding, antivirus scanning).
- **There is no login yet.** If you expose an upload endpoint, anyone who can reach the API can upload (within the size and type limits). Add authorization when authentication exists.
- **No built-in quotas.** One client can fill the disk with many allowed uploads.

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/Storage/IFileStorage.cs` | **New.** `IFileStorage`, `IFileStorageManager` and convenience extensions (bytes, text) |
| `i25Core.Core/Storage/StoragePath.cs` | **New.** Validates and normalizes paths (shared by every driver) |
| `i25Core.Core/Storage/StoredFile.cs` | **New.** Size, modified time, content type, URL |
| `i25Core.Core/Storage/ContentTypes.cs` | **New.** Content type from an extension |
| `i25Core.Infrastructure/Storage/LocalFileStorage.cs` | **New.** The Local driver |
| `i25Core.Infrastructure/Storage/FileStorageManager.cs` | **New.** Creates and caches disks; `IStorageDriverFactory` and the Local factory |
| `i25Core.Infrastructure/Storage/StorageOptions.cs` | **New.** `Storage` settings |
| `i25Core.Web/FileStorage/StorageServiceExtensions.cs` | **New.** `AddI25Storage` and `UseI25Storage` (serves public disks) |
| `i25Core.Web/FileStorage/FileUploader.cs` | **New.** `IFileUploader` |
| `Program.cs`, `appsettings.json`, `.gitignore` | Registration, settings, runtime folders |

No database changes and no new packages.

### Writing a file safely

```
PutAsync("invoices/1.pdf", stream)
   1. StoragePath.Normalize         reject "..", drive letters, bad names -> 400
   2. Resolve against the disk root, then check the result is still inside the root
   3. overwrite = false and it exists?  -> 409
   4. write to  invoices/.{guid}.i25tmp   (same folder, so the final move is instant)
   5. move the temp file over invoices/1.pdf   (retried briefly if Windows reports it busy)
   6. delete the temp file if anything failed
```

Readers see either the old complete file or the new complete file, never a half-written one. Files ending in `.i25tmp` are reserved and never appear in listings.

### Design decisions

| Decision | Reason |
|---|---|
| **One small interface (`IFileStorage`)** | Application code never learns whether files are on a disk or in the cloud. A new driver implements ten methods. |
| **Path rules live in Core (`StoragePath`)** | Every driver, current and future, rejects the same bad paths the same way |
| **Atomic write (temp file, then move)** | A crash or a parallel reader can never see a corrupt file |
| **Short retry on the final move** | On Windows the move can briefly fail while another request is replacing or reading the same file. Without the retry, concurrent overwrites failed most of the time in testing. |
| **Readers open files with `FileShare.Delete`** | Otherwise a download in progress would block an overwrite or delete of the same file. The reader simply finishes the old content. |
| **Uploads get random names and never overwrite** | The user can't pick a path, replace someone else's file or inject `../` |
| **Configured lists replace defaults** | .NET configuration binding normally *appends* to a list that already has items. A user who tightened `AllowedExtensions` to `[".pdf"]` would silently still allow the defaults, which is a security hole. Defaults are applied only when the setting is absent. |
| **Every disk is created at startup** | A missing `Root`, an unregistered driver or an unwritable folder stops the app when you deploy, not when the first customer uploads |
| **Public disk served with `nosniff` and `sandbox`** | Uploaded files are untrusted input |
| **No upload controller shipped** | A ready-made, unauthenticated "upload anything" endpoint would be risky in a boilerplate. `IFileUploader` plus the example above is the safe building block. |
| **Web source folder is `FileStorage`, not `Storage`** | On Windows `Storage` and the runtime `storage/` folder are the same name, and `.gitignore` would hide the source. They are kept apart. |

### Adding a driver (S3, Azure)

1. Write a class implementing `IFileStorage` for the service. Run every incoming path through `StoragePath.Normalize(...)` first.
2. Write a factory:

```csharp
public class S3StorageDriverFactory : IStorageDriverFactory
{
    public string Driver => "S3";

    public IFileStorage Create(string diskName, DiskOptions options, StorageOptions storage)
    {
        var bucket = options.Settings["Bucket"];
        return new S3FileStorage(diskName, bucket /* ... */);
    }
}
```

3. Register it in `Program.cs`: `builder.Services.AddSingleton<IStorageDriverFactory, S3StorageDriverFactory>();`
4. Configure a disk: `"cloud": { "Driver": "S3", "Url": "https://cdn.example.com", "Settings": { "Bucket": "my-bucket" } }`

Application code stays the same.

---

## Limitations

| Limitation | Notes |
|---|---|
| Only the Local driver is included | S3 and Azure are not implemented. The extension point is above. |
| No time-limited ("signed") URLs | Private files need your own download endpoint (example above) |
| Under extreme contention on **one** file, an overwrite can still fail | The final move is retried for about 1.5 seconds. Uploads never overwrite (random names), so this affects only code that repeatedly rewrites the same path under constant heavy reading. |
| Windows paths ignore case | `A/B.txt` and `a/b.txt` are the same file on Windows and different files on Linux. Pick one case convention. |
| Local disks are per server | With several servers behind a load balancer, use a shared folder or a cloud driver |
| Temporary files from a crash are not cleaned up | They end in `.i25tmp`, are hidden from listings and are harmless to delete |
| Content is not scanned | See [Security](#security) |

---

## Testing performed

These were run against the API on a temporary SQL Server database with a temporary test controller (removed afterwards). About 130 checks in 4 batches. Four checks in the first batch failed because of mistakes in my test script (Git Bash rewriting `/` and re-encoding text, and an expectation of 200 where ASP.NET returns 204 for a null result). Each was re-verified by hand and the code behaved correctly.

| Area | Result |
|---|---|
| **Basic operations** on the private disk: put, overwrite, `overwrite=false`, read, exists, info, copy, move, list (sorted, no temp files), delete, delete folder | All correct, with 409 / 404 / 400 where documented |
| **Path attacks**: 22 patterns (`../`, `..\`, mixed, drive letters, `.`, reserved names, `x.i25tmp`, trailing dot or space, 300-character names, `:` `?` `*` `\|`) | All **400**. A leading `/` stays inside the disk (`/etc/passwd` is stored as `etc/passwd`). Nothing was written outside the disk folder. |
| **Unicode and special characters** (`ñandú café #1.txt`) | Stored and read back correctly; the URL is escaped (`my%20file%231.txt`) |
| **Public disk**: URL generation, fetching in a browser, headers | Served with the right content type, `nosniff` and `sandbox` |
| **Not served**: unknown extensions, dot-files, private-disk files, `..` and encoded `%2e%2e` traversal out of `/storage` | All **404**; private content did not leak |
| **Uploads**: random 32-character names, folder, original name, extension from file, private disk, unknown disk | Correct |
| **Upload rules**: `.exe`, `photo.jpg.exe`, `.php`, no extension, `EVIL.PNG`, empty file, 2 MB over a 1 MB limit, 900 KB under it | Rejected or accepted as documented |
| **Client tricks**: file name `..\..\evil.png`; claimed type `text/html` for a `.png` | Name reduced to `evil.png`, stored under a random name; type taken from the extension |
| **Integrity**: 900 KB binary upload then download; 40 MB streamed in and out | Identical SHA-256 |
| **Concurrency**: 8 parallel writers to one path (8 runs) | Always exactly one complete winner, one file, no temp leftovers |
| **Read while overwriting**: 3 MB file replaced repeatedly while read 150 times (6 runs on the final code, about 500 writes) | 0 partial or mixed reads, 0 read errors, 0 write errors in the final runs |
| **Configuration**: bad default disk, shared URL, URL `/`, URL under `/api`, missing `Root`, unregistered driver | Each stops the app at startup with a clear message |
| **Configuration**: unregistered driver | Error names the driver and lists the available ones |
| **Configuration**: CDN-style absolute URL | URLs built, nothing served locally |
| **Configuration**: `AllowedExtensions` set to `[".pdf"]` / key absent / `["*"]` | Only `.pdf` accepted / defaults apply / any extension accepted |

### Defects found and fixed during testing

1. **Configured lists were merged with the defaults**, so tightening `AllowedExtensions` had no effect and the error message listed every extension twice. Fixed (see design decisions).
2. **Concurrent overwrites failed on Windows** (about 90% of writes in the stress test). Fixed with the short retry and by letting readers share delete access.
3. **A bad disk setting only failed when first used.** Fixed: every disk is now created at startup.
4. **The `.gitignore` rule `storage/` also hid the source folders named `Storage`**, so the first commit contained none of the new code. Fixed by anchoring the rule to the runtime folders.

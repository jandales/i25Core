# Using i25Core as a library (NuGet packages)

i25Core is published as four NuGet packages, so another project (for example i25Builder) can use it without copying code.

| Package | What is inside | Depends on |
|---|---|---|
| `i25Core.Core` | Base entity, contracts, exceptions, the `ApiResponse` envelope | - |
| `i25Core.Infrastructure` | `I25DbContext` (EF Core, SQL Server, audit trail, model events), services behind auth, mail, storage, caching, jobs, localization, and the built-in messages | Core |
| `i25Core.BusinessLayer` | Generic repository and business layer, paging, dynamic filters and sorting | Core, Infrastructure |
| `i25Core.AspNetCore` | **Everything for the web layer** and the built-in controllers. This is the one you reference. | the three above |

Referencing `i25Core.AspNetCore` brings the other three automatically.

## 1. Build the packages

```powershell
.\pack.ps1                  # version from Directory.Build.props (1.0.0)
.\pack.ps1 -Version 1.1.0   # or choose one
```

The packages (and symbol packages) go to `artifacts/packages/` (ignored by git). Change the version for every release;
a package version cannot be replaced once someone has restored it.

## 2. Make them available to other projects

**Quickest: a local folder.** In the consuming solution add a `nuget.config`:

```xml
<configuration>
  <packageSources>
    <add key="i25core-local" value="C:\projects\i25Core\artifacts\packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

**Shared with the team: GitHub Packages** (private to your account or organisation):

```powershell
# once: a personal access token with the write:packages scope
dotnet nuget add source https://nuget.pkg.github.com/jandales/index.json --name github --username jandales --password <TOKEN> --store-password-in-clear-text
# publish
dotnet nuget push "artifacts/packages/*.nupkg" --source github --api-key <TOKEN>
```

Never commit the token. In CI use the built-in `GITHUB_TOKEN`. A `RepositoryUrl` is already set in the packages, so
GitHub links them to the repository.

## 3. Use it in a project

```powershell
dotnet new web -n MyApi
cd MyApi
dotnet add package i25Core.AspNetCore --version 1.0.0
```

`Program.cs`:

```csharp
using i25Core.AspNetCore;

return I25CoreApp.Run(args);
```

Then copy `i25Core.Web/appsettings.json` from this repository into the project (it lists every setting with its
default) and set at least the connection string and, outside Development, `Auth:Jwt:Key`
(see `docs/authentication.md`). That is a working API: sign-in, roles, health checks, rate limiting, logging,
Swagger (Development), and so on.

Need more control than `Run`? It is only these pieces:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddI25Core();                                  // all services
builder.Services.AddScoped<IMyService, MyService>();   // yours
var app = builder.Build();
app.UseI25Core();                                      // pipeline in the right order + controllers + health endpoints
app.MapGet("/hello", () => "hi");                      // yours, after the core pipeline
app.Run();
```

`I25CoreApp.Run` adds a bootstrap logger and the startup-error handling on top, so startup failures are structured logs.
`Run` takes `configureServices` (the builder), `configurePipeline` (the app, after the core pipeline) and
`configureOptions`.

### Options (`I25CoreOptions`)

| Option | Default | Meaning |
|---|---|---|
| `ConnectionStringName` | `DefaultConnection` | Which connection string to use |
| `IncludeControllers` | `true` | The built-in Auth, Users, Roles, AuditLog, Locale and Notification controllers. `false` = services only, you write the controllers |
| `EnableSwagger` | `true` | Swagger generation and (Development) UI |
| `ScanAssemblies` | the application | Assemblies searched for FluentValidation validators and event listeners, besides the library |

## 4. Your own tables: your own DbContext

The library's tables (Users, Roles, tokens, audit log, notifications) live in `I25DbContext`. To add yours, inherit it:

```csharp
public class Product : BaseEntity { public string Name { get; set; } = ""; public decimal Price { get; set; } }

public class AppDbContext : I25DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, IEventDispatcher? events = null, ICurrentUser? currentUser = null)
        : base(options, events, currentUser) { }

    public DbSet<Product> Products => Set<Product>();
}
```

```csharp
return I25CoreApp.Run<AppDbContext>(args);      // or builder.AddI25Core<AppDbContext>()
```

- The library asks for `I25DbContext` and receives your `AppDbContext` (same instance), so audit logging, events and
  transactions cover your entities too, and `BaseController<Product>` / `IBusinessLayer<Product>` just work.
- **Migrations belong to your project.** Run `dotnet ef migrations add Initial` in your project: it creates the library
  tables **and** yours. The library's own migrations are not applied to your database. When you upgrade the packages and a
  new version adds columns or tables, add a new migration in your project.
- Roles `Admin` and `User` are part of the model (seed data), so they arrive with your first migration.
- Declare entities as `DbSet` properties on your context (or with `modelBuilder` calls in your `OnModelCreating`, after `base`).

## 5. Messages and settings

- The built-in texts (errors, authentication, messages) are embedded in the package, so no `lang/` folder is needed.
  Add your own `lang/en/messages.json` to your project: its keys are added to, and replace, the built-in ones
  (tested: a project override changes the "created" message). See `docs/localization.md`.
- All settings keep their defaults inside the packages. Anything you put in your own `appsettings.json` wins.

## What stays in this repository

`i25Core.Web` is now only a sample application: `Program.cs` is one line and `appsettings.json` documents every setting.
It is not packaged. Use it to try changes (`dotnet run`) before packing.

## Moving i25Builder onto the packages

1. Add the package source and `i25Core.AspNetCore`.
2. Replace its copied projects (`I25Builder.Core`, `Infrastructure`, `BusinessLayer`) with the packages; keep its own
   entities, controllers and validators.
3. Make its `I25DbContext` inherit the package's `I25DbContext` (rename it, for example `BuilderDbContext`) and keep its `DbSet`s.
4. Replace the long `Program.cs` with `I25CoreApp.Run<BuilderDbContext>(args)`.
5. **Existing database:** the package expects tables such as `Users`, `Roles`, `AuditLog`. If i25Builder already has
   tables with the same names, compare them first, review the SQL of the generated migration, and try it on a copy of the
   database before production. Do not apply a "create everything" migration to a database that already has the tables:
   edit it to create only what is missing, or baseline it.

## Decisions and limitations

- **Namespace change:** the web-layer namespace is now `i25Core.AspNetCore.*` (it was `i25Core.Web.*`).
- **One package to reference** (`i25Core.AspNetCore`), four to publish. Splitting the web layer further (auth only, jobs
  only) is not done: the features share one pipeline.
- **SQL Server only** (the context and Hangfire storage use it).
- **No licence is set** in the packages yet; choose one before sharing them outside the company
  (`PackageLicenseExpression` in `Directory.Build.props`).
- **No automatic publishing:** a CI workflow that packs and pushes on a tag is a separate step.
- **Versioning is manual** (`Directory.Build.props` or `-Version`). Breaking changes should bump the major version.
- The library is secure by default: every endpoint needs a token unless marked `[AllowAnonymous]`, including your own
  controllers (shown in the test: `/api/Products` answers 401 without a token).
- Hangfire, Serilog, Swagger, StackExchange.Redis and the other libraries come along as dependencies of
  `i25Core.AspNetCore`; their versions are set by the package.
- The `transactions` feature (a separate branch) adds one line to `ServiceCollectionExtension`; when it is merged, keep that
  line inside the generic `AddI25Infrastructure<TContext>` method.

## Testing performed

A scratch application was created **outside this repository**, referencing only the packages from a local folder feed:

| Case | Result |
|---|---|
| Four packages build with `pack.ps1`; the new application restores and builds from them | passed |
| A one-line `Program.cs` (`I25CoreApp.Run<AppDbContext>`) starts the whole stack | passed |
| Its own endpoint added after `UseI25Core` works | passed |
| `dotnet ef migrations add` in the consuming project creates the 8 library tables plus its `Products` table | passed |
| Library sign-in, `/api/Auth/me`, `/api/Users` (admin) on the consumer's database | passed |
| `BaseController<Product>`: create and paged list; secure by default (401 without a token) | passed |
| Automatic audit trail records the consumer's entity | passed |
| An event listener in the consumer's own assembly runs | passed |
| Spanish error text with no `lang` folder in the consumer (embedded files) | passed |
| The consumer's `lang/en/messages.json` overrides a built-in message | passed |
| Health, security headers, correlation id, Swagger document (library and consumer controllers) and UI | passed |
| `IncludeControllers = false`: the built-in Auth controller is gone, the consumer's controller still works | passed |
| The sample `i25Core.Web` after the move: health, sign-in, `/api/Users`, Spanish messages, CORS preflight, Swagger, the auth rate limit (429), and no pending model changes | passed |

Not covered: publishing to GitHub Packages, i25Builder itself (its existing database), a package upgrade from 1.0.0 to a
newer version with a schema change, and running the packages on a database other than SQL Server.

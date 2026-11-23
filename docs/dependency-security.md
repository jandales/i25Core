# Dependency security

How i25Core keeps NuGet packages free of known vulnerabilities, and what was fixed.

## Check it

```powershell
dotnet list i25Core.Web/i25Core.Web.csproj package --vulnerable --include-transitive
```

"has no vulnerable packages" is the goal. Run it for every project (`i25Core.Core`, `i25Core.Infrastructure`,
`i25Core.BusinessLayer`, `i25Core.Web`). It reads the NuGet vulnerability database, so it needs internet access.
`dotnet build` also warns (`NU1903`, `NU1904`) when a package has a known advisory.

## Fixed: Newtonsoft.Json 11.0.1 (GHSA-5crp-9r3c-p9vr, high)

- **Cause:** `Hangfire.Core 1.8.14` asks for `Newtonsoft.Json >= 11.0.1`, and NuGet picks the lowest version that fits.
  The app never referenced Newtonsoft.Json directly, so it came in as a transitive package at the vulnerable version.
  The advisory concerns parsing deeply nested JSON (stack overflow, denial of service). Hangfire uses Newtonsoft.Json
  to store job arguments in the database.
- **Fix:** `i25Core.Infrastructure` (the project that references Hangfire) now has a direct
  `<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />`. A direct reference wins over the transitive
  minimum, and it flows to every project that uses Infrastructure (BusinessLayer, Web).
- **Why 13.0.3:** the first release without the advisory (13.0.1 and later) with the latest patch; no API change that
  Hangfire depends on (it is built for Newtonsoft.Json 11 and runs on 13).

## Rules for adding packages

- Prefer the newest stable version, and run the check above after adding or updating a package.
- When a vulnerable package appears only as a dependency of another one, add a direct reference to the fixed version
  in the project that owns the parent package, like above. Remove it once the parent package requires a safe version.
- Do not suppress `NU190x` warnings.

## Testing performed

| Check | Result |
|---|---|
| `dotnet list package --vulnerable --include-transitive` before the change | Newtonsoft.Json 11.0.1, high |
| ... after the change, for Core, Infrastructure, BusinessLayer and Web | no vulnerable packages |
| Build | 0 errors |
| The app ships `Newtonsoft.Json.dll` 13.0.3 | yes |
| Hangfire still works on 13.0.3: app starts, the recurring cleanup job is registered, a queued job (the password-reset email from forgot-password) runs and writes its `.eml` file, 1 job succeeded and 0 failed | passed |
| `/health/ready` | 200 |

Not covered: a long soak of all job types. Only the recurring registration and one queued job were exercised.

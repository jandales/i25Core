# Authentication and Roles

People sign in with an email and a password and get a short-lived **access token** (a JWT) to send with every request. A longer-lived **refresh token** gets them a new access token without typing the password again. Every account has **roles** (`Admin`, `User`, or your own), and endpoints can require a role.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Sign up, sign in, sign out | `/api/Auth/register`, `/login`, `/logout` | Breeze / Sanctum |
| API tokens | JWT access token + rotating refresh token | Sanctum / Passport tokens |
| Protect an endpoint | `[Authorize]` | `auth:sanctum` middleware |
| Require a role | `[Authorize(Roles = "Admin")]` | Gates / Spatie roles |
| Who is calling? | `ICurrentUser` | `auth()->user()` |
| Forgot / reset password | `/api/Auth/forgot-password`, `/reset-password` | Password reset |
| Manage users and roles | `/api/Users`, `/api/Roles` (admins only) | Admin panel |
| Audit log shows who did it | `ChangedBy` = the signed-in user | Auditing packages |

---

## Quick start

1. **Set a signing key.** It signs the access tokens, so keep it secret and never commit it:
   ```powershell
   dotnet user-secrets init --project i25Core.Web
   dotnet user-secrets set "Auth:Jwt:Key" "put-a-long-random-secret-of-at-least-32-characters-here" --project i25Core.Web
   ```
   In **Development only**, an empty key makes a temporary one (everyone is signed out when the app restarts). In any other environment an empty or short key **stops the app at startup**.

2. **Create the first administrator.** Set these once (environment variables or user secrets), start the app, then remove the password:
   ```
   Auth__BootstrapAdmin__Email=admin@yourcompany.com
   Auth__BootstrapAdmin__Password=a-strong-passphrase-here
   ```
   It is created only when **no administrator exists**, so leaving it configured does no harm. If the password is too weak, the error is logged and the app keeps running.

3. **Sign in:**
   ```http
   POST /api/Auth/login
   { "email": "admin@yourcompany.com", "password": "a-strong-passphrase-here" }
   ```
   ```json
   {
     "success": true,
     "data": {
       "tokenType": "Bearer",
       "accessToken": "eyJhbGciOi...",
       "accessTokenExpiresAt": "2026-11-23T10:15:00Z",
       "refreshToken": "kX3...43 characters...",
       "refreshTokenExpiresAt": "2026-12-07T10:00:00Z",
       "user": { "id": 1, "email": "admin@yourcompany.com", "name": "Administrator", "roles": ["Admin", "User"] }
     }
   }
   ```

4. **Call the API** with the access token:
   ```http
   GET /api/Auth/me
   Authorization: Bearer eyJhbGciOi...
   ```
   In Swagger, click **Authorize** and paste the access token (without the word `Bearer`).

---

## Everything is closed by default

A new endpoint nobody thought about **requires a signed-in user**. To open one, mark it:

```csharp
[AllowAnonymous]
[HttpGet("prices")]
public IActionResult Prices() => Ok(...);
```

| Endpoint | Access |
|---|---|
| `/api/Auth/register`, `login`, `refresh`, `logout`, `forgot-password`, `reset-password` | Open |
| `/api/Locale` | Open (a sign-in screen needs the language list) |
| Swagger, the `/storage` public files, the jobs dashboard (`/jobs`, local machine only) | Open or own rule; they are not API endpoints |
| `/api/Auth/me`, `logout-all`, `change-password` | Any signed-in user |
| `/api/Notification/User/{id}` | The signed-in user's own inbox; admins can read any |
| `/api/AuditLogContoller` | Admins only |
| `/api/Users`, `/api/Roles` | Admins only |
| Everything else (your entity controllers) | Any signed-in user |

Set `Auth:RequireAuthenticatedByDefault` to `false` to make endpoints open unless marked `[Authorize]`.

An unauthenticated request for a URL that doesn't exist also gets **401**, not 404, so callers can't tell what exists.

### Requiring a role

```csharp
[Authorize(Roles = "Admin")]                  // one role
[Authorize(Roles = "Admin,Auditor")]          // any of these
[Authorize(Policy = "AdminOnly")]             // the built-in policy for Admin
public class ReportsController : ControllerBase { ... }
```

Or check in code:

```csharp
public class VendorController : BaseController<Vendor>
{
    private readonly ICurrentUser _me;

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!_me.IsInRole(Roles.Admin)) throw new ForbiddenException();
        ...
    }
}
```

### `ICurrentUser`

Inject it anywhere (controllers, business layers, listeners):

| Member | |
|---|---|
| `IsAuthenticated` | `true` when someone is signed in |
| `Id`, `Email`, `Name` | The user's details (`null` when nobody is signed in) |
| `Roles` / `IsInRole("Admin")` | The user's roles |

Outside a web request (a background job) nobody is signed in: `IsAuthenticated` is `false`.

---

## The endpoints

### Accounts

| Request | What it does |
|---|---|
| `POST /api/Auth/register` | Creates an account with the `User` role. Closed with `Auth:AllowRegistration = false` (then only admins create accounts). |
| `POST /api/Auth/login` | Returns the tokens. |
| `POST /api/Auth/refresh` | `{ "refreshToken": "..." }` returns a new access token **and a new refresh token**. Each refresh token works **once**. |
| `POST /api/Auth/logout` | `{ "refreshToken": "..." }` ends that session. Always answers OK. |
| `POST /api/Auth/logout-all` | Ends every session of the signed-in user. |
| `GET /api/Auth/me` | The signed-in user's profile. |
| `POST /api/Auth/change-password` | `{ "currentPassword", "newPassword" }`. Signs out every other device and returns a fresh session for this one. |
| `POST /api/Auth/forgot-password` | `{ "email" }` sends a reset email. The answer is the same whether or not the address has an account. |
| `POST /api/Auth/reset-password` | `{ "email", "token", "newPassword" }` uses the token from the email. Signs out every device. |

### Administration (Admin role)

| Request | What it does |
|---|---|
| `GET /api/Users?search=&role=&page=1&pageSize=20` | Lists users (paged, capped at 100 per page). |
| `GET /api/Users/{id}` | One user. |
| `POST /api/Users` | Creates a user (`email`, `password`, `name`, `roles`, `isActive`). Default role: `User`. |
| `PUT /api/Users/{id}` | Changes the name, or switches the account on or off. A switched-off account is signed out everywhere. |
| `PUT /api/Users/{id}/roles` | `{ "roles": ["User", "Auditor"] }` replaces the roles. |
| `POST /api/Users/{id}/unlock` | Clears a lockout. |
| `DELETE /api/Users/{id}` | Deletes the user, their role links and their tokens. |
| `GET /api/Roles`, `POST /api/Roles`, `DELETE /api/Roles/{id}` | List, create and delete roles. |

Safety rules built in:

- You cannot deactivate or delete **your own** account.
- You cannot delete, deactivate or demote the **last active administrator**.
- `Admin` and `User` cannot be deleted, and a role still assigned to someone cannot be deleted.
- Role names are 2–64 letters, digits, `_` or `-`, starting with a letter.

### Using it from a front-end app

```
login ───► keep accessToken in memory, refreshToken in secure storage
every call ───► Authorization: Bearer {accessToken}
a call returns 401 "token expired" ───► POST /refresh with the refresh token
                                        ├─ OK: save BOTH new tokens, repeat the call
                                        └─ 401: sign in again
```

Always save the **new** refresh token. The old one stops working the moment it is used.

---

## Passwords

| Rule | Default |
|---|---|
| Length | 10 to 128 characters |
| Not a very common password | A built-in list of 50 (`password123`, `qwerty`, ...) |
| Does not contain the email name | For names of 4 or more characters |
| More than two different characters | `aaaaaaaaaaaa` is refused |
| Spaces, accents, emoji | Allowed and kept exactly as typed |

There are no "must contain a symbol" rules, on purpose: length matters more, and forced symbols make people choose predictable passwords.

Passwords are stored only as salted **PBKDF2** hashes (ASP.NET's own password hasher, 210,000 rounds by default). A hash made with fewer rounds is upgraded automatically at the user's next sign-in.

### Lockout

After **5 wrong passwords in a row** the account is locked for **15 minutes** (`Auth:Lockout`). Wrong guesses through `change-password` count too. While locked, even the correct password gets **429**, with a message saying how long to wait. An admin can unlock it, and a password reset clears it.

### Not telling attackers anything

- An unknown email and a wrong password give the **same message**, and take **about the same time** (the server does the same amount of hashing for both).
- `forgot-password` answers the same for every address.
- "This account is disabled" is shown only to someone who knew the correct password.

---

## Tokens

### Access token (JWT)

Signed with HMAC-SHA256 and valid for **15 minutes**. It contains `sub` (user id), `email`, `name`, `role` (one per role), `jti`, `iss`, `aud`, `iat`, `nbf` and `exp`. It never contains the password or its hash.

Every request checks the signature, the issuer, the audience and the expiry (with 30 seconds of allowed clock difference). Only `HS256` is accepted, so `alg: none` and algorithm-swap tricks fail.

> **Because it is stateless, an access token works until it expires**, even if the user is deactivated or loses a role in between. That is at most 15 minutes. Roles change at the next refresh. `GET /api/Auth/me` always checks the database, so it refuses a deactivated or deleted user at once.

### Refresh token

A random 256-bit value, valid **14 days**. Only its **SHA-256 hash** is stored, so a database leak does not hand out usable tokens.

It is **rotated**: each refresh returns a new refresh token and uses up the old one. If an already-used token is presented again, someone has a copy, so the **whole session is revoked** and the event is logged as a warning. Two simultaneous refreshes with the same token let exactly one succeed.

| Event | Effect on refresh tokens |
|---|---|
| Logout | That session ends (even via an old token of it) |
| Logout all, password change, password reset | Every session ends |
| User deactivated or deleted | Their tokens stop working |
| Refresh token reuse | That session ends |
| A nightly job (03:00 UTC) | Deletes tokens that can never be used again |

### Password-reset token

A 256-bit one-time token, hashed in the database, valid **60 minutes**. Asking for a new link cancels the older one. A weak new password is refused **before** the token is used, so a typo doesn't burn the link.

---

## Configuration

`appsettings.json`:

```json
"Auth": {
  "AllowRegistration": true,
  "RequireAuthenticatedByDefault": true,
  "RefreshTokenDays": 14,
  "ResetTokenMinutes": 60,
  "ResetPasswordUrl": "",
  "Jwt":      { "Key": "", "Issuer": "i25Core", "Audience": "i25Core", "AccessTokenMinutes": 15 },
  "Password": { "MinLength": 10, "MaxLength": 128, "HashIterations": 210000 },
  "Lockout":  { "MaxFailedAttempts": 5, "Minutes": 15 },
  "BootstrapAdmin": { "Email": "", "Password": "", "Name": "Administrator" }
}
```

| Setting | Description |
|---|---|
| `AllowRegistration` | `false` when only admins should create accounts |
| `RequireAuthenticatedByDefault` | `true` (recommended): unmarked endpoints need a sign-in |
| `ResetPasswordUrl` | Your front-end's reset page, e.g. `https://app.example.com/reset?token={token}&email={email}`. Empty: the email shows the token only. |
| `Jwt:Key` | The signing secret, at least 32 characters. **Set it with user secrets or an environment variable (`Auth__Jwt__Key`), never in this file.** |
| `Jwt:Issuer` / `Audience` | Names written into and checked on every token |
| `Jwt:AccessTokenMinutes` | Access-token lifetime. Keep it short. |
| `Password:HashIterations` | PBKDF2 rounds (minimum 10,000). Higher is slower for attackers and for you. |
| `Lockout:MaxFailedAttempts` | `0` switches locking off |

Run behind a proxy or load balancer? Configure forwarded headers so the recorded client address is the real one.

---

## Messages and languages

Every message (sign-in errors, password rules, the reset email, admin messages) comes from `lang/{locale}/auth.json`, in English and Spanish, and follows the request's language. See the localization docs.

## Audit log

Changes are recorded with the **signed-in user's id and name** in `ChangedBy` / `ChangedByName`. Changes made outside a request (background jobs) or before sign-in (registration) record `0`.

Kept **out** of the audit log: password hashes, sign-in bookkeeping (last login, failed counts, lockouts), and the token tables.

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/Auth/AuthContracts.cs` | **New.** `Roles`, `ICurrentUser`, request and response models, `IAuthService`, `IUserAdminService` |
| `i25Core.Core/Entities/User.cs`, `Role.cs`, `AuthTokens.cs` | **New.** `User` (also a notification receiver), `Role`, `UserRole`, `RefreshToken`, `PasswordResetToken` |
| `i25Core.Core/Configuration/AuthMaps.cs` | **New.** Tables and unique indexes (one account per email, one role per name) |
| `i25Core.Core/Exceptions/AppException.cs` | `UnauthorizedException` (401), `TooManyRequestsException` (429) |
| `i25Core.Infrastructure/Auth/AuthService.cs` | **New.** Sign-up, sign-in, refresh, passwords |
| `i25Core.Infrastructure/Auth/UserAdminService.cs` | **New.** Users and roles administration with the safety rules |
| `i25Core.Infrastructure/Auth/PasswordService.cs` | **New.** Hashing, password rules, timing equalisation |
| `i25Core.Infrastructure/Auth/TokenHasher.cs`, `AuthOptions.cs` | **New.** Token creation/hashing and settings |
| `i25Core.AspNetCore/Auth/JwtTokens.cs` | **New.** Signing key and access-token creation |
| `i25Core.AspNetCore/Auth/AuthServiceExtensions.cs` | **New.** JWT validation, the secure-by-default policy, Swagger's Authorize button |
| `i25Core.AspNetCore/Auth/ApiAuthorizationResultHandler.cs` | **New.** 401 and 403 in the `ApiResponse` envelope |
| `i25Core.AspNetCore/Auth/HttpCurrentUser.cs`, `AuthValidators.cs`, `AuthBackground.cs` | **New.** Current user, request checks (incl. a strict email rule), first-admin bootstrap and the nightly cleanup |
| `i25Core.AspNetCore/Controllers/AuthController.cs`, `UsersController.cs` | **New.** The endpoints (`UsersController.cs` also holds `RolesController`) |
| `I25DbContext.cs`, `AuditTrail.cs` | Users and roles tables; the audit trail records the current user |
| `NotificationController.cs`, `AuditLogContoller.cs`, `LocaleController.cs` | Inbox restricted to its owner; audit log admin-only; locale list public |
| `Program.cs`, `appsettings.json`, `lang/*/auth.json` | Registration, settings, messages |
| `Migrations/…_AddAuthentication.cs` | **New.** Users, Roles, UserRoles, RefreshTokens, PasswordResetTokens; seeds the `Admin` and `User` roles |

Packages added: `Microsoft.AspNetCore.Authentication.JwtBearer` and `Microsoft.Extensions.Identity.Core` (8.0.34).

### Sign-in flow

```
POST /login ──► find user by normalized email
                 ├─ none: do a dummy hash anyway, answer "invalid email or password"
                 ├─ locked: 429
                 ├─ wrong password: count the failure (lock after 5), answer the same generic message
                 ├─ disabled (correct password only): 401 "account disabled"
                 └─ OK: reset counters, upgrade an old hash, make a refresh-token family
                        + an access token ──► respond

POST /refresh ──► hash the token, look it up
                   ├─ unknown / expired: 401
                   ├─ already used or revoked: revoke the whole family, 401, warning in the log
                   └─ OK: atomically claim it (UPDATE ... WHERE UsedAt IS NULL; one winner),
                          issue a new refresh token in the same family + a new access token
```

### Design decisions

| Decision | Reason |
|---|---|
| **Own small implementation, not ASP.NET Identity** | Identity brings its own tables and conventions. Here the tables follow `BaseEntity`, and only its proven password hasher is reused. |
| **Access token plus rotating refresh token** | A stolen access token dies in 15 minutes; a stolen refresh token is detected the first time it is used twice |
| **Refresh and reset tokens stored as hashes** | A database leak does not give attackers working tokens |
| **Atomic "claim" of a token (`UPDATE ... WHERE UsedAt IS NULL`)** | Two requests with the same token cannot both win |
| **Dummy hashing for unknown emails** | Prevents finding valid accounts by response time |
| **Secure by default** | A forgotten `[Authorize]` should not become an open endpoint |
| **Fail at startup for a missing or weak key** | A weak secret is found when deploying, not after an incident |
| **Development generates a temporary key** | The project runs with no setup, and the warning says why sessions end on restart |
| **Roles in the token** | No database call per request. The cost is that role changes apply at the next refresh. |
| **Never return the `User` entity** | Responses use `UserProfile`, so a password hash cannot leak through a serializer |
| **`HS256` only** | Blocks algorithm-confusion attacks |
| **Password and token changes are transactions** | A failure cannot burn a reset link without changing the password |
| **Strict email rule** | FluentValidation's built-in check only looks for an `@` |
| **Hangfire dashboard placed before authorization** | The secure-by-default policy applies to every request that reaches authorization; the dashboard has its own local-only rule |

---

## Limitations

| Limitation | Notes |
|---|---|
| Access tokens cannot be revoked | They work until they expire (15 minutes). `/me` checks the database. Shorten `AccessTokenMinutes` if that is too long. |
| Roles apply at the next token refresh | At most 15 minutes after an admin changes them |
| No rate limiting by IP address | Lockout protects each account, but an attacker can try many accounts. Add rate limiting (roadmap) and put the API behind a proxy that limits requests. |
| No two-factor authentication, no email confirmation, no social logins | |
| A client that refreshes twice at once (a bug or a retry) is signed out | That is how reuse detection tells a copy from a retry |
| `forgot-password` does slightly more work for real accounts | Responses are identical, but a precise timing measurement could still tell them apart |
| Emails with non-ASCII characters are rejected | The strict rule allows ASCII addresses only |
| The reset email depends on your mail settings | With the default `Log` driver it is written to `storage/mail` and not sent |
| Jobs dashboard stays local-only | It uses its own rule, not these tokens |
| The unversioned `/api/...` and `/api/v1/...` routes both work | Both are protected the same way |

---

## Testing performed

Tested end to end against a scratch SQL Server database with curl scripts (a temporary test controller and the database were removed afterwards).

| Area | Result |
|---|---|
| Registration, login, refresh rotation, reuse detection, logout, logout-all | passed |
| Password policy, lockout (429), generic invalid-credentials message | passed |
| Forgot / reset password (hashed one-time token, queued email) | passed |
| Roles CRUD, user admin, last-admin protection | passed |
| Forged / tampered / expired JWTs, `alg` confusion, skew window | passed |
| Startup refuses a missing or short signing key outside Development | passed |
| Production hides Swagger; Development uses an ephemeral key | passed |
| `AllowRegistration=false`, `RequireAuthenticatedByDefault=false` | passed |
| Password hash upgrade on sign-in | passed |
| Concurrent registration of one email (exactly one account) | passed |
| Cleanup job removes old tokens, keeps recent ones | passed |
| Bootstrap admin (weak password refused without logging it, good one created, no-op when an admin exists) | passed |
| Public static files stay public | passed |

Defects found while testing and fixed in this branch:

- Reset password consumed the token but never saved the new password. It now saves inside a transaction (change password too).
- `EmailAddress()` accepted `a'; DROP TABLE Users;--@x.com`; a strict email rule was added and emails are trimmed.
- Hangfire dashboard returned 401 under the fallback policy; it is now mapped before authentication.
- Sign-in bookkeeping fields (failed count, lockout, last login) no longer write audit rows.

Not covered: 2FA, email confirmation, per-IP rate limiting, immediate revocation of already-issued access tokens (they live up to 15 minutes).

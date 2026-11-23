# Mail and Notifications

i25Core can send email and deliver notifications to people through more than one channel.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Send an email now | `IMailer.SendAsync(message)` | `Mail::send(...)` |
| Send an email in the background | `IMailer.Queue(message)` | `Mail::queue(...)` / `->later(...)` |
| Email from an HTML template | `IMailer.FromTemplateAsync("welcome", data)` | Blade / Markdown mailables |
| Tell a person something, by email and/or in-app | `INotificationService.SendAsync(receiver, notification)` | `$user->notify(new X)` |
| An in-app inbox (unread, mark as read) | `INotificationInbox` + `NotificationController` | `$user->unreadNotifications`, `markAsRead()` |
| Add a channel (SMS, Slack, push) | Implement `INotificationChannel` | Custom notification channel |
| Develop without sending real mail | `Mail:Driver = Log` writes `.eml` files | `MAIL_MAILER=log` |

---

## Configuration

`appsettings.json`:

```json
"Mail": {
  "Driver": "Log",
  "FromAddress": "no-reply@localhost",
  "FromName": "i25Core",
  "Queue": "default",
  "TemplatePath": "EmailTemplates",
  "LogPath": "storage/mail",
  "Smtp": {
    "Host": "localhost",
    "Port": 587,
    "Security": "Auto",
    "Username": "",
    "Password": "",
    "TimeoutSeconds": 30
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Driver` | `Log` | `Log`: writes `.eml` files, **never contacts a server** (safe default). `Smtp`: real delivery. `None`: discards everything. A misspelled value stops the app at startup. |
| `FromAddress` / `FromName` | `no-reply@localhost` / `i25Core` | Default sender. A message can override it with `From`. |
| `Queue` | `default` | Queue used by `IMailer.Queue`. It must be listed in `BackgroundJobs:Queues`. |
| `TemplatePath` | `EmailTemplates` | Folder of templates, relative to the app root |
| `LogPath` | `storage/mail` | Where the Log driver writes `.eml` files (already in `.gitignore`) |
| `Smtp:Host` / `Port` | `localhost` / `587` | Mail server |
| `Smtp:Security` | `Auto` | `Auto`, `None`, `StartTls`, `StartTlsWhenAvailable` or `SslOnConnect` |
| `Smtp:Username` / `Password` | empty | Login. Leave `Username` empty for servers without login. |
| `Smtp:TimeoutSeconds` | `30` | Connection and command timeout |

**Keep real passwords out of `appsettings.json`.** Use an environment variable (`Mail__Smtp__Password`) or [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (`dotnet user-secrets set "Mail:Smtp:Password" "..."`).

**Typical production setup:**
```
Mail__Driver=Smtp
Mail__Smtp__Host=smtp.yourprovider.com
Mail__Smtp__Port=587
Mail__Smtp__Security=StartTls
Mail__Smtp__Username=...
Mail__Smtp__Password=...
Mail__FromAddress=no-reply@yourdomain.com
```

---

## 1. Sending mail

Inject `IMailer` (from `i25Core.Core.Mail`):

```csharp
var message = new EmailMessage
{
    To = { new EmailAddress("ana@example.com", "Ana") },
    Cc = { new EmailAddress("manager@example.com") },
    Subject = "Your order shipped",
    HtmlBody = "<p>Good news!</p>",
    TextBody = "Good news!",                     // optional: makes it a multipart message
    Attachments =
    {
        new EmailAttachment { FileName = "invoice.pdf", ContentType = "application/pdf", Content = pdfBytes }
    }
};

await _mailer.SendAsync(message);                // sends now; throws if the server refuses it
string jobId = _mailer.Queue(message);           // sends in the background; returns at once
_mailer.Queue(message, TimeSpan.FromMinutes(10)); // ...after a delay
```

| | `SendAsync` | `Queue` |
|---|---|---|
| When it sends | Before the call returns | Later, on a job worker |
| The user waits for the mail server | Yes | No |
| If the mail server is down | The call throws | The job is **retried automatically** |
| Survives an app restart | No | Yes, stored in the database |
| Needs background jobs | No | Yes (`AddI25BackgroundJobs`) |

`Queue` checks the message first, so an obviously broken message (no recipient, bad address) fails in the request instead of silently failing inside a job.

**Attachments and queued mail:** a queued message is stored as JSON in the job table, so attachments are stored there too (as base64). Keep them small, or send large files with `SendAsync`, or link to a download instead.

### Rules

- At least one recipient (`To`, `Cc` or `Bcc`) and at least one body (`HtmlBody` or `TextBody`).
- A malformed address, a missing recipient or a missing body throws `ArgumentException`, which the API returns as **400** with a clear message.
- `Bcc` recipients receive the mail but are not shown in the headers.

---

## 2. Email templates

Put an HTML file in the `EmailTemplates` folder (and an optional `.txt` file with the same name):

`EmailTemplates/welcome.html`
```html
<html>
  <head><title>Welcome, {{Name}}!</title></head>
  <body>
    <p>Hello {{Name}}, your account is ready.</p>
    <div>{{{Footer}}}</div>
  </body>
</html>
```

`EmailTemplates/welcome.txt` (optional plain-text version)
```
Hello {{Name}}, your account is ready.
```

```csharp
var message = await _mailer.FromTemplateAsync("welcome", new { Name = vendor.Name, Footer = "<small>Thanks</small>" });
message.To.Add(new EmailAddress(vendor.Email));
_mailer.Queue(message);
```

| Syntax | Meaning |
|---|---|
| `{{Name}}` | Value **HTML-encoded**. Always use this for user data: `<script>` becomes `&lt;script&gt;`, so users can't inject markup. |
| `{{{Footer}}}` | Value inserted **as is**. Only for HTML you wrote yourself. |
| `<title>…</title>` | Becomes the email subject (placeholders work there too). Set `message.Subject` afterwards to override. |
| `welcome.txt` | If present, becomes the plain-text body (placeholders not encoded) |

- Data can be an object (`new { Name = "Ana" }`) or a `Dictionary<string, object?>`. Names ignore case.
- A placeholder with no value is an error (400), so a typo can't send "Hello !" to customers.
- Template names allow letters, digits, `_`, `-` and sub-folders (`orders/shipped`). Names containing `..` or other characters are rejected (400), so a template name can't read arbitrary files.
- A missing template file returns 500, because it is a configuration problem, not a caller mistake.

---

## 3. Notifications

A notification is a message for one person that can go out through several channels. Built in: **`mail`** and **`database`** (an in-app inbox).

### Step 1: make the receiver notifiable

Implement `INotifiable` on an entity (a User, a Vendor, …):

```csharp
public class Vendor : BaseEntity, INotifiable
{
    public string? Email { get; set; }
    // ...

    public int NotifiableId => id;                        // get-only: EF does not map these
    public string? RouteNotificationForMail => Email;     // null = no mail for this receiver
}
```

`NotifiableType` defaults to the class name (`"Vendor"`). Notifications are stored against that type and id.

### Step 2: write the notification

```csharp
using i25Core.Core.Mail;
using i25Core.Core.Notifications;

public class VendorApprovedNotification : Notification
{
    private readonly string _approvedBy;
    public VendorApprovedNotification(string approvedBy) => _approvedBy = approvedBy;

    // Which channels to use
    public override IReadOnlyList<string> Via(INotifiable n) =>
        new[] { NotificationChannels.Mail, NotificationChannels.Database };

    // Content for the mail channel (To is filled in automatically)
    public override EmailMessage? ToMail(INotifiable n) => new()
    {
        Subject = "Your account was approved",
        HtmlBody = $"<p>Approved by {System.Net.WebUtility.HtmlEncode(_approvedBy)}.</p>"
    };

    // Content for the database channel: any object that becomes JSON
    public override object? ToDatabase(INotifiable n) => new { message = "Account approved", by = _approvedBy };
}
```

### Step 3: send it

```csharp
await _notifications.SendAsync(vendor, new VendorApprovedNotification("Ana"));      // one receiver
await _notifications.SendAsync(vendors, new VendorApprovedNotification("Ana"));     // many receivers
await _notifications.SendAsync(new AnonymousNotifiable("guest@example.com"),        // an address only
                               new InvoiceReadyNotification());
```

### How it behaves

| Rule | Detail |
|---|---|
| **Mail is queued by default** | Override `public override bool QueueMail => false;` to send mail before `SendAsync` returns |
| **Receiver has no email** | The mail channel is skipped (a warning is logged); other channels still run |
| **`ToMail` / `ToDatabase` returns `null`** | That channel is skipped |
| **The mail's `To` is always the receiver** | Whatever `ToMail` puts in `To` is replaced by `RouteNotificationForMail` |
| **Anonymous receiver** | Mail works; the database channel is skipped because there is no inbox |
| **One channel fails** | The other channels still run. The error is rethrown at the end (several failures become one `AggregateException`). |
| **Unknown channel name in `Via`** | Throws, listing the available channels. A typo can't silently send nothing. |

### The in-app inbox

Notifications sent through the `database` channel are stored in a `Notifications` table. Read them with `INotificationInbox`, or through the ready-made API:

| Request | Result |
|---|---|
| `GET /api/Notification/{type}/{id}` | The receiver's notifications, newest first. Query: `unreadOnly=true`, `take=50` (maximum 200) |
| `GET /api/Notification/{type}/{id}/unread-count` | Number of unread notifications |
| `POST /api/Notification/{type}/{id}/{notificationId}/read` | Mark one as read (repeating it is fine) |
| `POST /api/Notification/{type}/{id}/read-all` | Mark all as read; returns how many changed |
| `DELETE /api/Notification/{type}/{id}/{notificationId}` | Delete one |

Example: `GET /api/Notification/Vendor/17?unreadOnly=true`
```json
{
  "success": true,
  "data": [
    {
      "id": 5,
      "type": "VendorApprovedNotification",
      "data": { "message": "Account approved", "by": "Ana" },
      "readAt": null,
      "createdAt": "2026-11-22T13:57:47Z"
    }
  ]
}
```

A notification that belongs to a different receiver (or type) returns **404**, so one receiver can't mark or delete another's notifications by guessing ids.

> **Security note:** the API has no login yet, so these endpoints take the receiver from the URL. Anyone who can reach the API can read any receiver's inbox. When authentication is added, change the controller to read the receiver from the signed-in user.

### Adding a channel (SMS, Slack, push)

```csharp
public class SmsChannel : INotificationChannel
{
    public string Name => "sms";

    public async Task SendAsync(INotifiable receiver, Notification notification, CancellationToken ct)
    {
        // read your own content method from the notification, then call your SMS provider
    }
}

// Program.cs
builder.Services.AddScoped<INotificationChannel, SmsChannel>();
```

Then list `"sms"` in a notification's `Via`.

---

## Using it with events

When the **events** feature is merged, a listener is the natural place to send notifications, so controllers stay simple:

```csharp
[Queued]
public class NotifyVendorOnApproval : IEventListener<VendorApproved>
{
    private readonly I25DbContext _db;
    private readonly INotificationService _notifications;
    // ...
    public async Task HandleAsync(VendorApproved e, CancellationToken ct)
    {
        var vendor = await _db.Set<Vendor>().FindAsync(new object[] { e.VendorId }, ct);
        await _notifications.SendAsync(vendor!, new VendorApprovedNotification(e.ApprovedBy), ct);
    }
}
```

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/Mail/EmailMessage.cs` | **New.** `EmailMessage`, `EmailAddress`, `EmailAttachment` (plain JSON-friendly data) |
| `i25Core.Core/Mail/IMailer.cs` | **New.** `IMailer` and `IEmailTransport` |
| `i25Core.Core/Notifications/NotificationContracts.cs` | **New.** `Notification`, `INotifiable`, `AnonymousNotifiable`, `INotificationChannel`, `INotificationService`, `INotificationInbox`, `NotificationDto` |
| `i25Core.Core/Entities/DatabaseNotification.cs` | **New.** The stored notification (excluded from the audit log) |
| `i25Core.Core/Configuration/DatabaseNotificationMap.cs` | **New.** Table `Notifications` and its inbox index |
| `i25Core.Infrastructure/Mail/Mailer.cs` | **New.** `IMailer`: send, queue, template |
| `i25Core.Infrastructure/Mail/SmtpEmailTransport.cs` | **New.** Real delivery with MailKit |
| `i25Core.Infrastructure/Mail/LogEmailTransport.cs` | **New.** Log driver (`.eml` files) and the `None` driver |
| `i25Core.Infrastructure/Mail/MimeFactory.cs` | **New.** Builds and validates the MIME message |
| `i25Core.Infrastructure/Mail/EmailTemplateRenderer.cs` | **New.** `{{placeholder}}` templates |
| `i25Core.Infrastructure/Mail/QueuedMailRunner.cs` | **New.** What the job queue runs for a queued email |
| `i25Core.Infrastructure/Mail/MailOptions.cs` | **New.** `Mail` settings |
| `i25Core.Infrastructure/Notifications/*` | **New.** `NotificationService`, the two channels, `NotificationInbox` |
| `i25Core.AspNetCore/Mail/MailServiceExtensions.cs` | **New.** `AddI25Mail` (chooses the driver) and `AddI25Notifications` |
| `i25Core.AspNetCore/Controllers/NotificationController.cs` | **New.** Inbox API |
| `i25Core.Infrastructure/I25DbContext.cs`, `Program.cs`, `appsettings.json`, `.gitignore` | One mapping line, registration, settings, `storage/` |
| `Migrations/…_AddNotifications.cs` | **New.** Creates the `Notifications` table |
| `i25Core.Infrastructure.csproj` | `MailKit` 4.18.2 |

### Flows

```
Mail
 IMailer.SendAsync(msg) ──► IEmailTransport (chosen by Mail:Driver)
                                 ├─ Smtp : MailKit → connect → (login) → send → disconnect
                                 ├─ Log  : write storage/mail/*.eml
                                 └─ None : discard

 IMailer.Queue(msg) ──► validate now ──► JSON ──► IBackgroundJobService.Enqueue<QueuedMailRunner>
                                                        │  (later, on a worker)
                                                        ▼
                                                  transport.SendAsync(msg)   ── throws on failure ──► Hangfire retries

Notification
 INotificationService.SendAsync(receiver, notification)
    for each channel in notification.Via(receiver):
        find INotificationChannel by name   ── unknown name → error
          ├─ "mail"     → ToMail → To = receiver's address → Queue (or SendAsync if QueueMail = false)
          └─ "database" → ToDatabase → JSON → INSERT Notifications
        (a failing channel doesn't stop the others; errors are rethrown at the end)
```

### Design decisions

| Decision | Reason |
|---|---|
| **`Log` is the default driver** | A fresh checkout, a test run or a developer's laptop can never email real customers by accident. Real delivery needs an explicit `Driver = Smtp`. |
| **MailKit, not `System.Net.Mail.SmtpClient`** | Microsoft marks `SmtpClient` as not recommended for new code. MailKit is current and maintained. Version 4.18.2 was chosen because 4.9.0 has a known vulnerability. |
| **`IMailer` and `IEmailTransport` are separate** | The transport only delivers. Queuing, templates and validation live in one place, so a new transport (SendGrid, Amazon SES) means one small class. |
| **`EmailMessage` is plain data** | It serializes to JSON, so it can be queued; no live objects or streams are involved |
| **Queued mail reuses background jobs** | One queue, one dashboard, one retry policy. No second system. |
| **Notifications use named channels** | The Laravel model: a notification says *what* and *where*; each channel knows *how*. Adding SMS doesn't touch notification code. |
| **Mail `To` is forced to the receiver** | A notification can't accidentally send to someone else |
| **Database notifications are not audited** | They are messages, not business data, and the audit log would double in size |
| **`{{x}}` encodes, `{{{x}}}` doesn't** | Safe by default for user-supplied text. Raw output has to be written deliberately. |
| **Template names are whitelisted** | Stops `../../secret` style paths |
| **Validation errors are `ArgumentException`** | The existing exception handler turns them into 400s, with no new error handling |
| **`Set<DatabaseNotification>()` instead of a `DbSet` property** | Keeps `I25DbContext` changes to one mapping line |

---

## Limitations

| Limitation | Notes |
|---|---|
| The inbox API has no login | See the security note above |
| Queued mail is "at least once" | If a send succeeds but the job fails afterwards (for example a crash), a retry can send a duplicate |
| Attachments are stored in the job table when queued | Keep them small |
| One SMTP connection per message | Fine for ordinary volume. A large mailing (thousands) would need batching on one connection. |
| `SmtpClient` TLS is automatic by default (`Security: Auto`) | Set `StartTls` or `SslOnConnect` explicitly in production |
| No built-in layout or CSS inlining for templates | Each template is a full HTML file |
| The Log driver's `.eml` files show the `Bcc:` header | That is only the local file; real SMTP delivery never includes it in the headers |
| Real mail providers were not tested | SMTP was tested against a fake server. Provider quirks (rate limits, sender verification) are yours to verify. |
| Database notifications raise model events once events are merged | They are messages, so add `[NoModelEvents]` to `DatabaseNotification` if you don't want that |

---

## Testing performed

These were run against the API on a temporary SQL Server database, with the **Smtp** driver pointed at a small fake SMTP server inside the test app (so the real MailKit transport was exercised), plus temporary templates, a test notification and a test controller. All of it was removed afterwards.

| # | Case | Result |
|---|---|---|
| 1 | Immediate SMTP send with To (with name), Cc, Bcc, Reply-To, HTML + text, attachment | All delivered; Bcc in the envelope but not in headers; sender taken from `FromName`/`FromAddress` |
| 2 | SMTP login (`Username`/`Password`) | Credentials reached the server |
| 3 | `Queue` → background job → SMTP | Delivered once |
| 4 | `Queue` with a 5 s delay | Not delivered at 2 s; delivered after the next job poll |
| 5 | SMTP server rejects the first attempt (451) | Job went to **Scheduled**, was retried automatically, delivered once, final state **Succeeded** |
| 6 | No recipient, no body, malformed address, `Queue` of a bad message | All **400** with clear messages |
| 7 | Template: subject from `<title>`, placeholders, `{{{raw}}}`, `.txt` companion | Correct |
| 8 | Template with `<script>` in user data | Encoded to `&lt;script&gt;` |
| 9 | Template with a missing value / traversal name / unknown file | 400 / 400 / 500 |
| 10 | Notification with mail + database | Mail sent to the receiver's address; row stored with JSON data and class-name type |
| 11 | Inbox: list, unread count, mark read (twice), unread-only, mark all, delete | Correct; read time kept on repeat |
| 12 | Marking another receiver's / another type's notification | **404** |
| 13 | Receiver with no email | No mail, database row stored, warning logged |
| 14 | Anonymous (address-only) receiver | Mail delivered; database channel skipped |
| 15 | Many receivers | One stored row per receiver |
| 16 | Unknown channel name `sms` | 500 listing the available channels |
| 17 | `ToMail` throws | Error surfaced (500), but the database channel still ran |
| 18 | Log driver | Wrote `.eml` files for immediate and queued mail; nothing reached the SMTP server |
| 19 | None driver | 200, nothing written or sent |
| 20 | Misspelled `Mail:Driver` | App stops at startup: "Mail:Driver 'Smptp' is not valid. Use Smtp, Log or None." |

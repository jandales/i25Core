# Localization (multi-language messages)

> **Update (library packaging):** the built-in texts (`messages`, `errors`, `auth`) are now embedded in the
> `i25Core.Infrastructure` assembly (source: `i25Core.Infrastructure/Localization/lang/`), so a project needs no
> `lang/` folder to start. A `lang/` folder in your application is optional: its keys are added on top and replace the
> built-in ones with the same key. See `docs/library.md`.

The API can answer in the language of the person using it. Texts live in plain JSON files in a `lang/` folder, one folder per language. A request picks its language with `?lang=es`, a header, or the browser's `Accept-Language`, and everything the API says (success messages, error messages, validation messages, emails) comes back in that language.

| Need | i25Core | Laravel equivalent |
|---|---|---|
| Translation files | `lang/en/messages.json`, `lang/es/messages.json` | `lang/en/messages.php` |
| Translate a text | `translator.Get("messages.created")` | `__('messages.created')` |
| Fill in values | `"Hello, :name!"` + `new { name = "Ana" }` | `__('welcome', ['name' => 'Ana'])` |
| Plural forms | `translator.Choice("messages.apples", 5)` | `trans_choice('messages.apples', 5)` |
| Pick the request's language | `?lang=es`, `X-Locale` header, `Accept-Language` | `App::setLocale()` middleware |
| Fall back to another language | `Localization:FallbackLocale` | `fallback_locale` |
| Sentence keys | `lang/es.json` | `lang/es.json` |
| Per-language emails | `EmailTemplates/es/welcome.html` | Localized Blade views |

---

## What is translated out of the box

| What | Where it comes from |
|---|---|
| Success messages ("Record created successfully." ...) | `lang/{locale}/messages.json` |
| Error messages ("Record not found.", "An unexpected error occurred.", concurrency, duplicate, foreign-key, malformed JSON ...) | `lang/{locale}/errors.json` |
| The summary of an automatic validation failure ("Validation failed.") | `lang/{locale}/errors.json` |
| Built-in FluentValidation messages ("'Name' must not be empty.") | FluentValidation's own translations (about 40 languages) |
| Exceptions you throw with a key (`WithKey`) | your `lang` files |
| Email templates | `EmailTemplates/{locale}/...` |

English and Spanish are included. English is the reference: every key exists there.

---

## Language files

```
i25Core.Web/lang/
├── en/
│   ├── messages.json        keys: messages.created, messages.not_found, ...
│   └── errors.json          keys: errors.unexpected, errors.concurrency, ...
├── es/
│   ├── messages.json
│   └── errors.json
├── es-MX/                   optional: only what differs from "es"
│   └── messages.json
└── es.json                  optional flat file (see "Sentence keys")
```

`lang/en/messages.json`:
```json
{
  "created": "Record created successfully.",
  "not_found": "Record not found."
}
```

- The **file name** is the group: `messages.json` makes keys like `messages.created`.
- **Nested objects** become dotted keys: `{ "login": { "failed": "..." } }` in `auth.json` is `auth.login.failed`.
- Language folder names are language codes: `en`, `es`, `fr`, `pt-BR`. Anything else stops the app at startup.
- `//` comments, `/* */` comments and trailing commas are allowed in the files, so translators can leave notes.
- Files are read **once at startup**. Restart the app after editing them.
- The `lang` folder is published with the app.

### Sentence keys (`lang/es.json`)

Instead of inventing key names you can use the English sentence itself as the key:

```json
{ "Welcome back": "Bienvenido de nuevo" }
```
```csharp
translator.Get("Welcome back");     // "Bienvenido de nuevo" in Spanish; "Welcome back" in English
```
A sentence with no translation simply comes back unchanged, so this is safe to start using without any English file.

### Adding a language

1. Create `lang/fr/` and copy `en/messages.json` and `en/errors.json` into it.
2. Translate the **values**. Do not change the keys, and keep placeholders such as `:name` as they are.
3. Restart the app. `GET /api/Locale` now lists `fr`.

Missing keys fall back to English, so a half-finished language works.

---

## Using it in code

Inject `ITranslator` (from `i25Core.Core.Localization`):

```csharp
public class VendorController : BaseController<Vendor>
{
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var vendor = await _service.Approve(id);

        // Inside any controller that inherits BaseController, use the Translate helper:
        return OkResponse(vendor, Translate("vendors.approved", new { name = vendor.Name }));
    }
}
```

```csharp
public class WelcomeService
{
    private readonly ITranslator _t;
    public WelcomeService(ITranslator t) => _t = t;

    public string Greeting(User user) => _t.Get("welcome.hello", new { name = user.Name });
}
```

| Method | What it does |
|---|---|
| `Get(key, replace?, locale?)` | The translation, with placeholders filled. An unknown key returns the key itself (and logs a warning once). |
| `Choice(key, count, replace?, locale?)` | A plural form, see below |
| `Has(key, locale?)` | Whether the key exists |
| `All(locale?, group?)` | Every translation of a language, for front-end apps |
| `Locale` | The language in use for this request |
| `AvailableLocales` | The loaded languages |

The optional `locale` argument forces a language, for example to write to a user in *their* language rather than the current request's.

### Placeholders

| Written in the file | Value `maria` becomes |
|---|---|
| `:name` | `maria` |
| `:Name` | `Maria` (first letter upper-case) |
| `:NAME` | `MARIA` (all upper-case) |

A `:word` with no matching value is left as it is, so times (`10:30`), addresses (`http://...`) and words you didn't fill are not damaged. A value is inserted exactly as given (no HTML processing). Numbers use the invariant format (`1234.5`).

### Plural forms

```json
{
  "apples": "{0} no apples|{1} one apple|[2,19] a few apples (:count)|[20,*] :count apples",
  "items": "one item|:count items"
}
```
```csharp
translator.Choice("messages.apples", 0);    // "no apples"
translator.Choice("messages.apples", 1);    // "one apple"
translator.Choice("messages.apples", 7);    // "a few apples (7)"
translator.Choice("messages.apples", 50);   // "50 apples"
translator.Choice("messages.items", 3);     // "3 items"
```

- Forms are separated by `|`.
- `{0}` means exactly 0; `[2,19]` means 2 to 19; `[20,*]` means 20 or more.
- Forms with no condition are chosen by position: the first for 1, the second for anything else.
- `:count` is filled in automatically.

---

## How a request chooses its language

The first of these that names a language you have, in this order:

| # | Source | Example |
|---|---|---|
| 1 | Query string | `GET /api/Vendor?lang=es` |
| 2 | Header | `X-Locale: es` |
| 3 | The browser's `Accept-Language` | `Accept-Language: es-MX,es;q=0.9,en;q=0.8` (highest quality first) |
| 4 | `Localization:DefaultLocale` | `en` |

- Names are not case-sensitive. A regional name falls back to its language: `es-MX` is served with `es` (and with `es-MX` itself if you added that folder).
- A language you don't have is skipped, not an error: `?lang=xx` falls through to the next source.
- Every response carries `Content-Language: es`, so clients and caches know which language they got.
- Only the **UI language** changes. Number and date formatting inside the server stays as it was, so one client's language can never change how data is parsed or stored.

### Configuration

`appsettings.json`:
```json
"Localization": {
  "DefaultLocale": "en",
  "FallbackLocale": "en",
  "Path": "lang",
  "QueryParameter": "lang",
  "HeaderName": "X-Locale",
  "ReadAcceptLanguage": true,
  "SetContentLanguageHeader": true
}
```

| Setting | Default | Description |
|---|---|---|
| `DefaultLocale` | `en` | Language when a request names none that you support |
| `FallbackLocale` | `en` | Language consulted when a key is missing from the requested one |
| `Path` | `lang` | Folder of language files (relative to the app root, or absolute) |
| `QueryParameter` | `lang` | Query-string name. Empty switches it off. |
| `HeaderName` | `X-Locale` | Header name. Empty switches it off. |
| `ReadAcceptLanguage` | `true` | Use the browser's `Accept-Language` |
| `SetContentLanguageHeader` | `true` | Add `Content-Language` to responses |

The app **refuses to start** (with a message naming the file) when `DefaultLocale` or `FallbackLocale` has no files, the `lang` folder is missing or empty, a file is not valid JSON, a value is not text, or a folder isn't a language code.

---

## Translating your own messages

### Success messages

In a controller, translate with the helper:

```csharp
return OkResponse(vendor, Translate("vendors.created"));
```

A business layer that builds a `GetResponse` can name a key; the controller translates it for the request:

```csharp
return new GetResponse { Success = true, Data = rows, Message = "Rows loaded.", MessageKey = "vendors.loaded" };
```

### Error messages (exceptions)

Throw as before, and attach a key with `WithKey`. The English text stays for your logs; the client sees the translation:

```csharp
throw new BusinessRuleException("The vendor Acme is inactive.")
    .WithKey("errors.vendor_inactive", new { name = vendor.Name });
```
```json
// lang/en/errors.json
{ "vendor_inactive": "The vendor :name is inactive." }
// lang/es/errors.json
{ "vendor_inactive": "El proveedor :name está inactivo." }
```

- If the key doesn't exist, the exception's own English text is shown. An exception without a key keeps its own text.
- `NotFoundException`, `NotFoundException.ForEntity(...)` and `ForbiddenException` already carry keys (`errors.not_found`, `errors.entity_not_found`, `errors.forbidden`).

### Validation messages

- **FluentValidation** (what `BaseController` uses): built-in messages follow the request language automatically. For your own text, translate inside the rule:

```csharp
public class VendorValidator : AbstractValidator<Vendor>
{
    public VendorValidator(ITranslator t)
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage(_ => t.Get("vendors.code_required"));
    }
}
```

- **`[Required]` / `[MaxLength]` attributes**: the summary ("Validation failed.") is translated, but the per-field texts that .NET generates for attributes stay in English. Use FluentValidation for messages users will read.

### Emails

Put a version per language in a sub-folder. The shared file is used when there is no version for the language:

```
EmailTemplates/
├── welcome.html          ← used for every language without its own version
├── welcome.txt
└── es/
    ├── welcome.html      ← Spanish
    └── welcome.txt
```

```csharp
var message = await _mailer.FromTemplateAsync("welcome", new { Name = "Ana" });          // the request's language
var spanish = await _mailer.FromTemplateAsync("welcome", new { Name = "Ana" }, "es");    // a specific language
```

The lookup order is `es-MX/` then `es/` then the shared file.

> **Background work has no request.** A queued email, a scheduled job or an event listener running on a worker uses the **default** language. Pass the receiver's language explicitly: `FromTemplateAsync(..., user.Locale)` and `translator.Get(key, args, user.Locale)`.

> **Stored text stays in the language it was written in.** A database notification or a log entry saved as translated text is stuck in that language. Store the key and its values (`{ "key": "vendors.approved", "name": "Acme" }`) and translate when you read it.

---

## For front-end apps

A single-page app can fetch the same texts instead of keeping its own copy:

| Request | Result |
|---|---|
| `GET /api/Locale` | `{ "current": "es", "default": "en", "fallback": "en", "available": ["en", "es"] }` |
| `GET /api/Locale/messages?locale=es` | Every text, as `{ "messages.created": "Registro creado correctamente.", ... }` |
| `GET /api/Locale/messages?locale=es&group=messages` | Only the `messages` group |

Missing texts in a language are filled from the fallback language. An unknown language returns 404 (with a translated message). The versioned URLs (`/api/v1/Locale`) work too.

> Language files are readable through this endpoint. **Never put secrets in them.**

---

## How it is implemented

### Files

| File | Purpose |
|---|---|
| `i25Core.Core/Localization/ITranslator.cs` | **New.** The interface application code uses |
| `i25Core.Core/Exceptions/AppException.cs` | `MessageKey`, `MessageArgs`, `WithKey(...)`; `NotFoundException.ForEntity`; `ForbiddenException` has a key |
| `i25Core.Infrastructure/Localization/JsonTranslator.cs` | **New.** Loads the files, finds keys, fills placeholders |
| `i25Core.Infrastructure/Localization/PluralSelector.cs` | **New.** Picks the plural form |
| `i25Core.Infrastructure/Localization/LocalizationOptions.cs` | **New.** `Localization` settings |
| `i25Core.AspNetCore/Localization/LocalizationServiceExtensions.cs` | **New.** Registration, language detection, `Content-Language` |
| `i25Core.AspNetCore/Controllers/LocaleController.cs` | **New.** `/api/Locale` |
| `i25Core.Web/lang/{en,es}/{messages,errors}.json` | **New.** Shipped texts |
| `i25Core.AspNetCore/Middleware/GlobalExceptionHandler.cs` | Error messages come from the translator |
| `i25Core.AspNetCore/Controllers/BaseController.cs` | `Translate(...)` helper; success and error messages use keys |
| `i25Core.BusinessLayer/.../BaseBusinessLayer.cs`, `GetResponse.cs` | `MessageKey`; not-found uses `ForEntity` |
| `i25Core.Infrastructure/Mail/EmailTemplateRenderer.cs`, `IMailer.cs`, `Mailer.cs` | Per-language templates; optional `locale` argument |
| `Program.cs`, `appsettings.json` | Registration, settings, the validation-response fix below |

No database changes, no new packages.

### How a key is found

```
translator.Get("messages.created")      request language: es-MX
   try  es-MX   (lang/es-MX/messages.json)
   then es      (parent language)
   then the default language
   then the fallback language
   not found anywhere -> the key itself is returned, and one warning is logged
```

Everything is loaded into memory when the app starts, so a lookup is a dictionary read and the translator is safe to share between requests.

### Pipeline order

```
UseI25Localization   ← first: picks the language, sets Content-Language
UseExceptionHandler  ← so error messages are already in the right language
 ... everything else
```

### Design decisions

| Decision | Reason |
|---|---|
| **JSON files, not `.resx`** | `.resx` files are compiled into the app and awkward to edit; JSON can be edited by a translator, a Git diff is readable, and there is nothing to rebuild |
| **Laravel's layout and placeholders (`:name`, `\|` plurals)** | Familiar to the team, and easy for translators to read |
| **Group files plus flat files** | Named keys for app messages; whole sentences when you don't want to invent names |
| **Missing key returns the key, never an error** | A forgotten translation shows up as visible text in testing, instead of breaking a page |
| **Fallback language** | A language can be added and finished gradually |
| **Everything is read at startup** | Fast, no file access per request, and a broken file is found at deploy time |
| **Only the UI language follows the request, not number/date formatting** | Prevents a request header from changing how numbers and dates are parsed or stored |
| **Exceptions carry keys instead of translated text** | Business and Core code has no access to the request's language; the handler translates when it builds the response |
| **`Content-Language` captured at the start of the request** | Reading it later gave the wrong value for responses with no body, such as a plain 404 |
| **Plurals use explicit ranges** | Covers any language without bundling every language's grammar rules |
| **Email templates by sub-folder** | Same idea as `lang/`, and a missing language quietly uses the shared template |

---

## Limitations

| Limitation | Notes |
|---|---|
| Changes to language files need a restart | No file watching |
| Plural forms are "first = one, second = other" plus explicit ranges | Languages with several plural forms (Russian, Polish, Arabic) must spell them out as ranges. Full CLDR plural rules are not included. |
| `[Required]`-style attribute messages and model-binding messages stay English | Use FluentValidation for user-facing validation texts |
| Messages written in code as `ArgumentException` text (for example "Invalid filter column") stay English | They are developer-facing. Add `WithKey` exceptions if a user can see them. |
| Swagger documentation is English only | |
| Background work uses the default language | Pass the receiver's language explicitly |
| Stored text is not retranslated | Store keys, not translated sentences |
| Numbers in placeholders use the invariant format | `1234.5`, not `1.234,5` |
| Language codes must be real culture codes | Don't turn on `InvariantGlobalization` in the app, or language lookups fail |
| Language files are public through `/api/Locale/messages` | No secrets in them |

---

## Testing performed

These were run against the API on a temporary SQL Server database, with extra temporary language files (placeholders, plurals, nested keys, a regional `es-MX` folder, a flat `es.json`), per-language email templates and a temporary controller. All of it was removed afterwards. 82 request checks plus 27 configuration and startup checks.

| Area | Result |
|---|---|
| **Choosing a language**: `?lang`, `X-Locale`, `Accept-Language` (quality order, regional fallback), unsupported language, empty or hostile values (`../../etc/passwd`, `<script>`), precedence between sources | Correct in every case; hostile values fall back to the default |
| **Number and date culture** under `?lang=es` | Unchanged; only the UI language changed |
| **API messages in English and Spanish**: create, list, list via `/get`, not found, update of a missing record, malformed JSON | All translated |
| **Exceptions**: concurrency 409, `NotFoundException` (with `:entity` and `:id`), `ForEntity`, `ForbiddenException`, `WithKey` with placeholders, an exception with no key, a key that doesn't exist | Translated; falls back to the exception's own text when there is no key or no translation |
| **500 errors** | Translated; Development still appends the technical detail |
| **Validation**: FluentValidation built-in messages, custom message through the translator, `[Required]` failures | Built-in and custom messages in Spanish; attribute field texts stay English (documented); summary translated |
| **Placeholders**: `:name`, `:Name`, `:NAME`, a time `10:30`, a URL, an unknown `:missing`, HTML in a value | As documented |
| **Lookup**: nested keys, fallback to English, `Has`, unknown key, flat `es.json`, `es-MX` over `es`, explicit `locale` argument | Correct |
| **Plurals**: `{0}`, `{1}`, `[2,19]`, `[20,*]`, simple `one\|other`, three plain forms, unknown key, in two languages | Correct |
| **Missing keys** | Logged once, not on every request |
| **Front-end API**: `/api/Locale`, messages by language and group, merging `es-MX` over `es`, unknown language 404 (translated), versioned URL | Correct |
| **Email templates**: English, Spanish, `es-MX` using `es`, explicit locale, language with no folder, a hostile locale string | Correct; the plain-text version comes from the same folder |
| **Startup checks**: default or fallback language with no files, missing or empty `lang` folder, malformed JSON, a number instead of text, a JSON array, folder names `english` and `x_y!` | Each stops the app with a message naming the file or setting |
| **Tolerant parsing**: `//` and `/* */` comments and a trailing comma | Loaded |
| **Settings**: `DefaultLocale=es`, `QueryParameter` empty or renamed, `HeaderName` empty or renamed, `ReadAcceptLanguage=false`, `SetContentLanguageHeader=false` | Each behaves as documented |
| **`Content-Language`** on 200, 404 (body and no body), 500 and Swagger responses | Correct |

### Defects found and fixed during testing

1. **`Content-Language` was wrong on responses with no body** (a plain 404 with `?lang=es` reported `en`). It is now read at the start of the request.
2. **The automatic model-validation response was never in your `ApiResponse` envelope.** `Program.cs` configured it *before* `AddControllers()`, so MVC's own setup replaced it. Responses for failures such as `[Required]` came back in the framework's default format (`type`, `title`, `errors`), even though the code and its comment say they should use the envelope. This was already the case in `main`. It is now applied with `PostConfigure` (one word) so the summary can be translated. **This changes the response shape for `[Required]`-style failures to the envelope.** To keep the old behaviour, change `PostConfigure` back to `Configure`.

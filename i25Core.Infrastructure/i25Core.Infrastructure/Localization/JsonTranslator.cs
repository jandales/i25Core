using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using i25Core.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Localization
{
    /// <summary>
    /// ITranslator backed by JSON files:
    /// <code>
    /// lang/en/messages.json   { "created": "Created." }          -> key "messages.created"
    /// lang/en/auth.json       { "login": { "failed": "..." } }   -> key "auth.login.failed"
    /// lang/es.json            { "Welcome": "Bienvenido" }        -> key "Welcome" (the English sentence)
    /// </code>
    /// Everything is read once at startup into memory, so lookups are fast and the object is thread-safe.
    /// Edit a language file and restart the app to see the change.
    /// </summary>
    public class JsonTranslator : ITranslator
    {
        private static readonly Regex Placeholder = new(@":([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex LocaleName = new(@"^[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

        // language -> (key -> text). Keys are compared ignoring case.
        private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);

        private readonly LocalizationOptions _options;
        private readonly ILogger<JsonTranslator> _logger;
        private readonly ConcurrentDictionary<string, byte> _reportedMissing = new(StringComparer.Ordinal);

        public JsonTranslator(IOptions<LocalizationOptions> options, ILogger<JsonTranslator> logger)
        {
            _options = options.Value;
            _logger = logger;

            LoadAll();
            Validate();
        }

        public IReadOnlyCollection<string> AvailableLocales => _languages.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        public string Locale => Resolve(null);

        public string Get(string key, object? replace = null, string? locale = null)
        {
            var text = Find(key, locale);

            if (text == null)
            {
                ReportMissing(key, locale);
                return key;
            }

            return Fill(text, ToDictionary(replace));
        }

        public string Choice(string key, int count, object? replace = null, string? locale = null)
        {
            var text = Find(key, locale);

            if (text == null)
            {
                ReportMissing(key, locale);
                return key;
            }

            var values = ToDictionary(replace) ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (!values.ContainsKey("count"))
                values["count"] = count;

            return Fill(PluralSelector.Select(text, count), values);
        }

        public bool Has(string key, string? locale = null) => Find(key, locale) != null;

        public IReadOnlyDictionary<string, string> All(string? locale = null, string? group = null)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var prefix = string.IsNullOrWhiteSpace(group) ? null : group.Trim().TrimEnd('.') + ".";

            // Fallback first, then the more specific languages on top of it.
            foreach (var language in Enumerable.Reverse(Chain(locale)))
            {
                foreach (var (key, text) in _languages[language])
                {
                    if (prefix == null || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result[key] = text;
                }
            }

            return result;
        }

        // ---------- lookup ----------

        private string? Find(string key, string? locale)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            foreach (var language in Chain(locale))
            {
                if (_languages[language].TryGetValue(key, out var text))
                    return text;
            }

            return null;
        }

        /// <summary>Loaded languages to try, best first: "es-MX", "es", then the default, then the fallback.</summary>
        private List<string> Chain(string? requested)
        {
            var chain = new List<string>(4);

            void Add(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name) && _languages.ContainsKey(name) && !chain.Contains(name, StringComparer.OrdinalIgnoreCase))
                    chain.Add(_languages.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)));
            }

            var name = NormalizeName(requested ?? CultureInfo.CurrentUICulture.Name);

            while (!string.IsNullOrEmpty(name))
            {
                Add(name);
                var dash = name.LastIndexOf('-');
                name = dash > 0 ? name[..dash] : null;
            }

            Add(_options.DefaultLocale);
            Add(_options.FallbackLocale);

            return chain;
        }

        /// <summary>The loaded language that best matches the request, or the default.</summary>
        private string Resolve(string? requested)
        {
            var name = NormalizeName(requested ?? CultureInfo.CurrentUICulture.Name);

            while (!string.IsNullOrEmpty(name))
            {
                if (_languages.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) is { } match)
                    return match;

                var dash = name.LastIndexOf('-');
                name = dash > 0 ? name[..dash] : null;
            }

            return _languages.Keys.First(k => string.Equals(k, _options.DefaultLocale, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeName(string name) => name.Replace('_', '-').Trim();

        // ---------- placeholders ----------

        private static string Fill(string text, IDictionary<string, object?>? values)
        {
            if (values == null || values.Count == 0 || !text.Contains(':'))
                return text;

            return Placeholder.Replace(text, match =>
            {
                var name = match.Groups[1].Value;

                if (!values.TryGetValue(name, out var value))
                    return match.Value;                       // not ours (a time like "10:30", a URL...): leave it

                var shown = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

                // :NAME -> upper case, :Name -> first letter upper case, :name -> as given
                if (name.Length > 1 && name.All(c => !char.IsLetter(c) || char.IsUpper(c)))
                    return shown.ToUpperInvariant();

                if (char.IsUpper(name[0]) && shown.Length > 0)
                    return char.ToUpperInvariant(shown[0]) + shown[1..];

                return shown;
            });
        }

        private static IDictionary<string, object?>? ToDictionary(object? values)
        {
            if (values == null)
                return null;

            if (values is IDictionary<string, object?> dictionary)
                return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);

            if (values is IReadOnlyDictionary<string, object?> readOnly)
                return new Dictionary<string, object?>(readOnly, StringComparer.OrdinalIgnoreCase);

            return values.GetType().GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToDictionary(p => p.Name, p => p.GetValue(values), StringComparer.OrdinalIgnoreCase);
        }

        private void ReportMissing(string key, string? locale)
        {
            // Once per key, so a missing translation in a busy endpoint doesn't flood the log.
            if (_reportedMissing.TryAdd(key, 0))
                _logger.LogWarning("Translation key '{Key}' was not found (language {Locale}).", key, locale ?? Locale);
        }

        // ---------- loading ----------

        private void LoadAll()
        {
            // 1. The messages that ship inside the library (errors, authentication, ...), so an application works with no lang folder.
            LoadEmbedded();

            // 2. The application's own lang/ folder. Its keys add to, and replace, the built-in ones.
            var root = _options.Resolve(_options.Path);

            if (!Directory.Exists(root))
                return;

            // lang/{locale}/{group}.json
            foreach (var folder in Directory.GetDirectories(root))
            {
                var language = ValidateLanguageName(Path.GetFileName(folder), folder);

                foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                    LoadFile(file, language, group: Path.GetFileNameWithoutExtension(file));
            }

            // lang/{locale}.json (keys are whole sentences)
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                var language = ValidateLanguageName(Path.GetFileNameWithoutExtension(file), file);
                LoadFile(file, language, group: null);
            }
        }

        // Embedded as "i25Core.lang/{locale}/{group}.json" (see the .csproj).
        private void LoadEmbedded()
        {
            var assembly = typeof(JsonTranslator).Assembly;

            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
            {
                var parts = name[EmbeddedPrefix.Length..].Replace('\\', '/').Split('/');

                if (parts.Length != 2 || !parts[1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var language = ValidateLanguageName(parts[0], name);

                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                LoadText(reader.ReadToEnd(), name, language, group: Path.GetFileNameWithoutExtension(parts[1]));
            }
        }

        private const string EmbeddedPrefix = "i25Core.lang/";

        private string ValidateLanguageName(string name, string path)
        {
            if (!LocaleName.IsMatch(name))
            {
                throw new InvalidOperationException(
                    $"'{path}' is not a valid language code. Name language folders and files like \"en\", \"es\" or \"pt-BR\".");
            }

            try
            {
                CultureInfo.GetCultureInfo(NormalizeName(name));
            }
            catch (CultureNotFoundException)
            {
                throw new InvalidOperationException($"'{path}' is not a language this system knows. Use a code like \"en\", \"es\" or \"pt-BR\".");
            }

            return NormalizeName(name);
        }

        private void LoadFile(string file, string language, string? group)
            => LoadText(File.ReadAllText(file, Encoding.UTF8), file, language, group);

        private void LoadText(string json, string file, string language, string? group)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(
                    json,
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Language file '{file}' is not valid JSON: {ex.Message}");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Language file '{file}' must contain a JSON object ({{ \"key\": \"text\" }}).");

                if (!_languages.TryGetValue(language, out var entries))
                    _languages[language] = entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                Flatten(document.RootElement, group, entries, file);
            }
        }

        private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string> into, string file)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = prefix == null ? property.Name : $"{prefix}.{property.Name}";

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        into[key] = property.Value.GetString() ?? string.Empty;
                        break;

                    case JsonValueKind.Object:
                        Flatten(property.Value, key, into, file);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Language file '{file}': '{key}' must be text or a group of texts, not {property.Value.ValueKind}.");
                }
            }
        }

        private void Validate()
        {
            if (_languages.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No language files were found. Add at least lang/{_options.DefaultLocale}/messages.json under '{_options.Resolve(_options.Path)}'.");
            }

            foreach (var (setting, value) in new[] { ("DefaultLocale", _options.DefaultLocale), ("FallbackLocale", _options.FallbackLocale) })
            {
                if (!_languages.ContainsKey(value))
                {
                    throw new InvalidOperationException(
                        $"Localization:{setting} is '{value}' but there are no files for it. Loaded languages: {string.Join(", ", _languages.Keys)}.");
                }
            }
        }
    }
}

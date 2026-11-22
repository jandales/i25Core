using System.Net;
using System.Text.RegularExpressions;
using i25Core.Core.Mail;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Mail
{
    /// <summary>
    /// Renders EmailTemplates/{name}.html (and optional {name}.txt) by replacing placeholders:
    /// {{Key}} is HTML-encoded (safe for user data), {{{Key}}} is inserted raw.
    /// </summary>
    public class EmailTemplateRenderer
    {
        private static readonly Regex Placeholder =
            new(@"\{\{\{\s*(\w+)\s*\}\}\}|\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);

        private static readonly Regex Title =
            new(@"<title>\s*(.*?)\s*</title>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // letters, digits, '_' and '-', optionally in sub-folders: "orders/shipped"
        private static readonly Regex SafeName =
            new(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*$", RegexOptions.Compiled);

        private readonly MailOptions _options;

        public EmailTemplateRenderer(IOptions<MailOptions> options)
        {
            _options = options.Value;
        }

        // "es", "pt-BR": the only language names that may become a folder name.
        private static readonly Regex SafeLanguage =
            new(@"^[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

        public async Task<EmailMessage> RenderAsync(string name, object? data, string? locale = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name) || !SafeName.IsMatch(name))
                throw new ArgumentException($"'{name}' is not a valid template name. Use letters, digits, '_' and '-'.");

            var folder = _options.Resolve(_options.TemplatePath);
            var htmlFile = FindTemplate(folder, name, locale)
                ?? throw new FileNotFoundException($"Email template '{name}' was not found at {Path.Combine(folder, name + ".html")}.");

            var values = ToDictionary(data);

            var html = Replace(await File.ReadAllTextAsync(htmlFile, cancellationToken), values, name, encode: true);

            var message = new EmailMessage { HtmlBody = html };

            var title = Title.Match(html);
            if (title.Success)
                message.Subject = WebUtility.HtmlDecode(title.Groups[1].Value);

            // The plain-text companion comes from the same folder as the HTML that was chosen.
            var textFile = Path.ChangeExtension(htmlFile, ".txt");
            if (File.Exists(textFile))
                message.TextBody = Replace(await File.ReadAllTextAsync(textFile, cancellationToken), values, name, encode: false);

            return message;
        }

        /// <summary>
        /// The template for the language, trying EmailTemplates/es-MX/welcome.html, then EmailTemplates/es/welcome.html,
        /// then EmailTemplates/welcome.html (the version for everyone).
        /// </summary>
        private static string? FindTemplate(string folder, string name, string? locale)
        {
            var language = (locale ?? System.Globalization.CultureInfo.CurrentUICulture.Name).Replace('_', '-').Trim();

            while (language.Length > 0)
            {
                if (SafeLanguage.IsMatch(language))
                {
                    var candidate = Path.Combine(folder, language, name + ".html");

                    if (File.Exists(candidate))
                        return candidate;
                }

                var dash = language.LastIndexOf('-');
                language = dash > 0 ? language[..dash] : string.Empty;
            }

            var shared = Path.Combine(folder, name + ".html");
            return File.Exists(shared) ? shared : null;
        }

        private static string Replace(string template, IDictionary<string, object?> values, string templateName, bool encode)
        {
            return Placeholder.Replace(template, match =>
            {
                var raw = match.Groups[1].Success;
                var key = raw ? match.Groups[1].Value : match.Groups[2].Value;

                if (!values.TryGetValue(key, out var value))
                    throw new ArgumentException($"Template '{templateName}' uses {{{{{key}}}}} but no value named '{key}' was supplied.");

                var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

                return encode && !raw ? WebUtility.HtmlEncode(text) : text;
            });
        }

        private static IDictionary<string, object?> ToDictionary(object? data)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (data == null)
                return result;

            if (data is IDictionary<string, object?> dictionary)
            {
                foreach (var pair in dictionary)
                    result[pair.Key] = pair.Value;

                return result;
            }

            foreach (var property in data.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
                result[property.Name] = property.GetValue(data);

            return result;
        }
    }
}

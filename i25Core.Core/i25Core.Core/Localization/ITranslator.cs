namespace i25Core.Core.Localization
{
    /// <summary>
    /// Looks up text in the language files under <c>lang/</c> (Laravel's __() and trans_choice()).
    /// The language is the current request's (see the Localization settings); outside a request it is the default.
    /// A key that exists nowhere is returned as it is, so a missing translation shows its key instead of failing.
    /// </summary>
    public interface ITranslator
    {
        /// <summary>
        /// Translates a key. <paramref name="key"/> is either "group.name" (from <c>lang/{locale}/group.json</c>)
        /// or a whole sentence from the flat file <c>lang/{locale}.json</c>.
        /// <paramref name="replace"/> fills placeholders: <c>Get("welcome.hello", new { name = "Ana" })</c> turns
        /// "Hello, :name!" into "Hello, Ana!" (":Name" capitalizes the first letter, ":NAME" upper-cases the value).
        /// <paramref name="locale"/> overrides the request's language, e.g. for an email to a user who speaks another one.
        /// </summary>
        string Get(string key, object? replace = null, string? locale = null);

        /// <summary>
        /// Translates a key that has plural forms, written as "one apple|many apples" or with explicit counts:
        /// "{0} no apples|{1} one apple|[2,19] a few apples|[20,*] many apples". ":count" is replaced automatically.
        /// </summary>
        string Choice(string key, int count, object? replace = null, string? locale = null);

        /// <summary>true when the key exists in the language (or a fallback language).</summary>
        bool Has(string key, string? locale = null);

        /// <summary>The language in use for this request: the best loaded match for the current UI culture ("es", "en", ...).</summary>
        string Locale { get; }

        /// <summary>The loaded languages, e.g. "en", "es".</summary>
        IReadOnlyCollection<string> AvailableLocales { get; }

        /// <summary>
        /// Every translation of a language (fallback language included), keyed "group.name". For front-end apps that
        /// want the strings themselves. <paramref name="group"/> limits the result to one group.
        /// </summary>
        IReadOnlyDictionary<string, string> All(string? locale = null, string? group = null);
    }
}

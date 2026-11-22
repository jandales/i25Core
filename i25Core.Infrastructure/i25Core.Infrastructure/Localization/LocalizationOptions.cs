namespace i25Core.Infrastructure.Localization
{
    /// <summary>Bound from the "Localization" section of appsettings.json.</summary>
    public class LocalizationOptions
    {
        public const string SectionName = "Localization";

        /// <summary>Language used when a request doesn't ask for a supported one.</summary>
        public string DefaultLocale { get; set; } = "en";

        /// <summary>Language consulted when a key is missing from the requested one. Usually the same as the default.</summary>
        public string FallbackLocale { get; set; } = "en";

        /// <summary>Folder holding the language files, relative to the app root (or absolute).</summary>
        public string Path { get; set; } = "lang";

        /// <summary>Query-string parameter that picks the language: ?lang=es. Empty to switch it off.</summary>
        public string QueryParameter { get; set; } = "lang";

        /// <summary>Request header that picks the language: X-Locale: es. Empty to switch it off.</summary>
        public string HeaderName { get; set; } = "X-Locale";

        /// <summary>Also honour the browser's Accept-Language header.</summary>
        public bool ReadAcceptLanguage { get; set; } = true;

        /// <summary>Add a Content-Language header to responses.</summary>
        public bool SetContentLanguageHeader { get; set; } = true;

        /// <summary>The app's content root. Set by AddI25Localization; not read from configuration.</summary>
        public string RootPath { get; set; } = string.Empty;

        public string Resolve(string path) =>
            System.IO.Path.IsPathRooted(path)
                ? System.IO.Path.GetFullPath(path)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    string.IsNullOrEmpty(RootPath) ? Directory.GetCurrentDirectory() : RootPath, path));
    }
}

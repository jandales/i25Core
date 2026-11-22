namespace i25Core.Infrastructure.Mail
{
    /// <summary>Bound from the "Mail" section of appsettings.json.</summary>
    public class MailOptions
    {
        public const string SectionName = "Mail";

        /// <summary>
        /// "Log" (default, safe: writes .eml files and never contacts a server),
        /// "Smtp" (real delivery) or "None" (discard).
        /// </summary>
        public string Driver { get; set; } = "Log";

        public string FromAddress { get; set; } = "no-reply@localhost";
        public string FromName { get; set; } = "i25Core";

        /// <summary>Queue used by IMailer.Queue. Must be listed in BackgroundJobs:Queues.</summary>
        public string Queue { get; set; } = "default";

        /// <summary>Folder (relative to the app root) holding {{placeholder}} templates.</summary>
        public string TemplatePath { get; set; } = "EmailTemplates";

        /// <summary>Folder (relative to the app root) where the Log driver writes .eml files.</summary>
        public string LogPath { get; set; } = "storage/mail";

        /// <summary>The app's content root. Set by AddI25Mail; not read from configuration.</summary>
        public string RootPath { get; set; } = string.Empty;

        public SmtpOptions Smtp { get; set; } = new();

        public string Resolve(string path) =>
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(string.IsNullOrEmpty(RootPath) ? Directory.GetCurrentDirectory() : RootPath, path);
    }

    public class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 587;

        /// <summary>Auto, None, StartTls, StartTlsWhenAvailable or SslOnConnect.</summary>
        public string Security { get; set; } = "Auto";

        public string? Username { get; set; }

        /// <summary>Keep real passwords out of appsettings.json: use user secrets or an environment variable (Mail__Smtp__Password).</summary>
        public string? Password { get; set; }

        public int TimeoutSeconds { get; set; } = 30;
    }
}

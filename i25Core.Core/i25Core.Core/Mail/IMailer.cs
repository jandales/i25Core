namespace i25Core.Core.Mail
{
    /// <summary>
    /// Sends email (Laravel's Mail facade). The transport is chosen by "Mail:Driver":
    /// Smtp (real delivery), Log (writes .eml files, nothing leaves the machine) or None.
    /// </summary>
    public interface IMailer
    {
        /// <summary>Sends now and waits for the mail server. Throws if sending fails. (Mail::send)</summary>
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends in the background through the job queue; returns the job id immediately.
        /// Failed sends are retried automatically. (Mail::queue / ->later)
        /// </summary>
        string Queue(EmailMessage message, TimeSpan? delay = null);

        /// <summary>
        /// Builds a message from an HTML template file (EmailTemplates/{name}.html, optional {name}.txt)
        /// by replacing {{Key}} placeholders with values from <paramref name="data"/>.
        /// The subject comes from the template's &lt;title&gt; unless you set it afterwards.
        /// A template can have a version per language (EmailTemplates/es/welcome.html); the request's language is used unless <paramref name="locale"/> names another.
        /// </summary>
        Task<EmailMessage> FromTemplateAsync(string templateName, object? data, string? locale = null, CancellationToken cancellationToken = default);
    }

    /// <summary>The low-level delivery used by IMailer. Implemented once per driver.</summary>
    public interface IEmailTransport
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
    }
}

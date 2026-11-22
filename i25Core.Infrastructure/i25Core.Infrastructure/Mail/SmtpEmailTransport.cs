using i25Core.Core.Mail;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Mail
{
    /// <summary>Real delivery over SMTP (Mail:Driver = "Smtp").</summary>
    public class SmtpEmailTransport : IEmailTransport
    {
        private readonly MailOptions _options;
        private readonly ILogger<SmtpEmailTransport> _logger;

        public SmtpEmailTransport(IOptions<MailOptions> options, ILogger<SmtpEmailTransport> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            var mime = MimeFactory.Create(message, _options);
            var smtp = _options.Smtp;

            if (!Enum.TryParse<SecureSocketOptions>(smtp.Security, ignoreCase: true, out var security))
                throw new InvalidOperationException(
                    $"Mail:Smtp:Security '{smtp.Security}' is not valid. Use Auto, None, StartTls, StartTlsWhenAvailable or SslOnConnect.");

            // One connection per message: simple and safe for background jobs. For bulk mail, batch in your own code.
            using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };

            await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _logger.LogInformation("Email '{Subject}' sent to {Recipients} via {Host}",
                message.Subject, string.Join(", ", message.To.Select(t => t.Address)), smtp.Host);
        }
    }
}

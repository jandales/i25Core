using i25Core.Core.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Mail
{
    /// <summary>
    /// Development driver (Mail:Driver = "Log"): nothing is sent. The message is written as a
    /// .eml file you can open in any mail program, and a line is logged.
    /// </summary>
    public class LogEmailTransport : IEmailTransport
    {
        private readonly MailOptions _options;
        private readonly ILogger<LogEmailTransport> _logger;

        public LogEmailTransport(IOptions<MailOptions> options, ILogger<LogEmailTransport> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            var mime = MimeFactory.Create(message, _options);

            var folder = _options.Resolve(_options.LogPath);
            Directory.CreateDirectory(folder);

            var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.eml");

            await using (var stream = File.Create(file))
                await mime.WriteToAsync(stream, cancellationToken);

            _logger.LogInformation("Mail (Log driver) '{Subject}' to {Recipients} written to {File}",
                message.Subject, string.Join(", ", message.To.Select(t => t.Address)), file);
        }
    }

    /// <summary>Mail:Driver = "None": accepts and discards every message.</summary>
    public class NullEmailTransport : IEmailTransport
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

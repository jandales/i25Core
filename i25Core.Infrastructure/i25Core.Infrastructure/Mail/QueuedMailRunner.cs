using System.Text.Json;
using Hangfire;
using i25Core.Core.Mail;

namespace i25Core.Infrastructure.Mail
{
    /// <summary>What the job queue runs for IMailer.Queue. A failure throws, so Hangfire retries it.</summary>
    public class QueuedMailRunner
    {
        private readonly IEmailTransport _transport;

        public QueuedMailRunner(IEmailTransport transport)
        {
            _transport = transport;
        }

        // Shown in the dashboard as "Mail: Welcome to ...".
        [JobDisplayName("Mail: {0}")]
        public async Task SendAsync(string label, string messageJson, CancellationToken cancellationToken)
        {
            var message = JsonSerializer.Deserialize<EmailMessage>(messageJson)
                ?? throw new InvalidOperationException("The queued email could not be read.");

            await _transport.SendAsync(message, cancellationToken);
        }
    }
}

using System.Text.Json;
using i25Core.Core.BackgroundJobs;
using i25Core.Core.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Mail
{
    public class Mailer : IMailer
    {
        private readonly IEmailTransport _transport;
        private readonly EmailTemplateRenderer _templates;
        private readonly IServiceProvider _services;
        private readonly MailOptions _options;

        public Mailer(
            IEmailTransport transport,
            EmailTemplateRenderer templates,
            IServiceProvider services,
            IOptions<MailOptions> options)
        {
            _transport = transport;
            _templates = templates;
            _services = services;
            _options = options.Value;
        }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            return _transport.SendAsync(message, cancellationToken);
        }

        public string Queue(EmailMessage message, TimeSpan? delay = null)
        {
            ArgumentNullException.ThrowIfNull(message);

            var jobs = _services.GetService<IBackgroundJobService>()
                ?? throw new InvalidOperationException(
                    "IMailer.Queue needs background jobs. Call AddI25BackgroundJobs, or use SendAsync instead.");

            // Fail now (in the request) for an obviously broken message, not later inside a job.
            MimeFactory.Create(message, _options);

            var label = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject;
            var json = JsonSerializer.Serialize(message);

            return delay is { } wait && wait > TimeSpan.Zero
                ? jobs.Schedule<QueuedMailRunner>(r => r.SendAsync(label, json, CancellationToken.None), wait, _options.Queue)
                : jobs.Enqueue<QueuedMailRunner>(r => r.SendAsync(label, json, CancellationToken.None), _options.Queue);
        }

        public Task<EmailMessage> FromTemplateAsync(string templateName, object? data, CancellationToken cancellationToken = default)
            => _templates.RenderAsync(templateName, data, cancellationToken);
    }
}

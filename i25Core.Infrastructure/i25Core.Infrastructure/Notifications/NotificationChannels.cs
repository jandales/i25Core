using System.Text.Json;
using i25Core.Core.Entities;
using i25Core.Core.Mail;
using i25Core.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace i25Core.Infrastructure.Notifications
{
    /// <summary>"mail" channel: sends Notification.ToMail to the receiver's RouteNotificationForMail.</summary>
    public class MailNotificationChannel : INotificationChannel
    {
        private readonly IMailer _mailer;
        private readonly ILogger<MailNotificationChannel> _logger;

        public MailNotificationChannel(IMailer mailer, ILogger<MailNotificationChannel> logger)
        {
            _mailer = mailer;
            _logger = logger;
        }

        public string Name => NotificationChannels.Mail;

        public async Task SendAsync(INotifiable notifiable, Notification notification, CancellationToken cancellationToken)
        {
            var message = notification.ToMail(notifiable);

            if (message == null)
                return;

            var address = notifiable.RouteNotificationForMail;

            if (string.IsNullOrWhiteSpace(address))
            {
                _logger.LogWarning("{Notification} skipped the mail channel: {Type} {Id} has no email address.",
                    notification.GetType().Name, notifiable.NotifiableType, notifiable.NotifiableId);
                return;
            }

            // The receiver is always the notifiable, whatever ToMail put in To.
            message.To = new List<EmailAddress> { new(address) };

            if (notification.QueueMail)
                _mailer.Queue(message);
            else
                await _mailer.SendAsync(message, cancellationToken);
        }
    }

    /// <summary>"database" channel: stores Notification.ToDatabase so the receiver can list it later.</summary>
    public class DatabaseNotificationChannel : INotificationChannel
    {
        private readonly I25DbContext _db;
        private readonly ILogger<DatabaseNotificationChannel> _logger;

        public DatabaseNotificationChannel(I25DbContext db, ILogger<DatabaseNotificationChannel> logger)
        {
            _db = db;
            _logger = logger;
        }

        public string Name => NotificationChannels.Database;

        public async Task SendAsync(INotifiable notifiable, Notification notification, CancellationToken cancellationToken)
        {
            var data = notification.ToDatabase(notifiable);

            if (data == null)
                return;

            // An address-only receiver has no row to attach an inbox to.
            if (notifiable is AnonymousNotifiable)
            {
                _logger.LogWarning("{Notification} skipped the database channel: an anonymous receiver has no inbox.",
                    notification.GetType().Name);
                return;
            }

            _db.Set<DatabaseNotification>().Add(new DatabaseNotification
            {
                Type = notification.GetType().Name,
                NotifiableType = notifiable.NotifiableType,
                NotifiableId = notifiable.NotifiableId,
                Data = JsonSerializer.Serialize(data)
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

using i25Core.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace i25Core.Infrastructure.Notifications
{
    public class NotificationService : INotificationService
    {
        private readonly Dictionary<string, INotificationChannel> _channels;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(IEnumerable<INotificationChannel> channels, ILogger<NotificationService> logger)
        {
            _channels = channels.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            _logger = logger;
        }

        public async Task SendAsync(INotifiable notifiable, Notification notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notifiable);
            ArgumentNullException.ThrowIfNull(notification);

            var errors = new List<Exception>();

            foreach (var name in notification.Via(notifiable).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // A typo in Via is a programming error: fail loudly, don't silently send nothing.
                if (!_channels.TryGetValue(name, out var channel))
                {
                    throw new InvalidOperationException(
                        $"{notification.GetType().Name} asks for the channel '{name}', which is not registered. " +
                        $"Available: {string.Join(", ", _channels.Keys)}.");
                }

                try
                {
                    await channel.SendAsync(notifiable, notification, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Channel {Channel} failed for {Notification} to {Type} {Id}",
                        name, notification.GetType().Name, notifiable.NotifiableType, notifiable.NotifiableId);
                    errors.Add(ex);
                }
            }

            if (errors.Count == 1)
                throw errors[0];

            if (errors.Count > 1)
                throw new AggregateException($"{errors.Count} notification channels failed.", errors);
        }

        public async Task SendAsync(IEnumerable<INotifiable> notifiables, Notification notification, CancellationToken cancellationToken = default)
        {
            foreach (var notifiable in notifiables)
                await SendAsync(notifiable, notification, cancellationToken);
        }
    }
}

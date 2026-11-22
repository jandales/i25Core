using i25Core.Core.Mail;

namespace i25Core.Core.Notifications
{
    /// <summary>Channel names used in <see cref="Notification.Via"/>.</summary>
    public static class NotificationChannels
    {
        public const string Mail = "mail";
        public const string Database = "database";
    }

    /// <summary>
    /// Something that can receive notifications (Laravel: the Notifiable trait).
    /// Implement it on an entity such as User or Vendor:
    /// <code>
    /// public int NotifiableId =&gt; id;
    /// public string? RouteNotificationForMail =&gt; Email;
    /// </code>
    /// </summary>
    public interface INotifiable
    {
        /// <summary>Primary key; used to store and find database notifications.</summary>
        int NotifiableId { get; }

        /// <summary>Email address for the mail channel. Null = this receiver gets no mail.</summary>
        string? RouteNotificationForMail { get; }

        /// <summary>Stored with database notifications. Defaults to the class name.</summary>
        string NotifiableType => GetType().Name;
    }

    /// <summary>A receiver that is only an email address, with no record behind it. (Laravel: Notification::route('mail', ...))</summary>
    public sealed class AnonymousNotifiable : INotifiable
    {
        public AnonymousNotifiable(string email) => RouteNotificationForMail = email;

        public int NotifiableId => 0;
        public string? RouteNotificationForMail { get; }
        public string NotifiableType => "Anonymous";
    }

    /// <summary>
    /// A message to a person through one or more channels (Laravel: a Notification class).
    /// Derive from it, list the channels in <see cref="Via"/> and build each channel's content.
    /// </summary>
    public abstract class Notification
    {
        /// <summary>Channels to use, e.g. <c>new[] { NotificationChannels.Mail, NotificationChannels.Database }</c>.</summary>
        public abstract IReadOnlyList<string> Via(INotifiable notifiable);

        /// <summary>Content for the mail channel. Return null to send no mail.</summary>
        public virtual EmailMessage? ToMail(INotifiable notifiable) => null;

        /// <summary>
        /// Content for the database channel: any object that serializes to JSON (usually an anonymous object).
        /// Return null to store nothing.
        /// </summary>
        public virtual object? ToDatabase(INotifiable notifiable) => null;

        /// <summary>true (default): mail is sent in the background through the job queue. false: sent before SendAsync returns.</summary>
        public virtual bool QueueMail => true;
    }

    /// <summary>Delivers a notification through one channel. Add your own (SMS, Slack, push) by implementing this.</summary>
    public interface INotificationChannel
    {
        /// <summary>The name used in <see cref="Notification.Via"/>.</summary>
        string Name { get; }

        Task SendAsync(INotifiable notifiable, Notification notification, CancellationToken cancellationToken);
    }

    /// <summary>Sends notifications (Laravel: $user->notify(...) / Notification::send(...)).</summary>
    public interface INotificationService
    {
        /// <summary>Sends through every channel listed in Via. If a channel fails the others still run; failures are rethrown together at the end.</summary>
        Task SendAsync(INotifiable notifiable, Notification notification, CancellationToken cancellationToken = default);

        /// <summary>Sends the same notification to many receivers. (Notification::send($users, ...))</summary>
        Task SendAsync(IEnumerable<INotifiable> notifiables, Notification notification, CancellationToken cancellationToken = default);
    }

    /// <summary>A stored (database channel) notification as returned to clients.</summary>
    public class NotificationDto
    {
        public int Id { get; set; }
        public string? Type { get; set; }

        /// <summary>The object returned by <see cref="Notification.ToDatabase"/>.</summary>
        public System.Text.Json.JsonElement? Data { get; set; }

        public DateTime? ReadAt { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>Reads and manages a receiver's stored notifications (Laravel: $user->notifications, ->unreadNotifications).</summary>
    public interface INotificationInbox
    {
        Task<List<NotificationDto>> GetAsync(string notifiableType, int notifiableId, bool unreadOnly = false, int take = 50, CancellationToken cancellationToken = default);

        Task<int> UnreadCountAsync(string notifiableType, int notifiableId, CancellationToken cancellationToken = default);

        /// <summary>Marks one notification read. false if it doesn't exist or belongs to someone else. (markAsRead)</summary>
        Task<bool> MarkAsReadAsync(string notifiableType, int notifiableId, int notificationId, CancellationToken cancellationToken = default);

        /// <summary>Marks every unread notification read; returns how many changed. (markAllAsRead)</summary>
        Task<int> MarkAllAsReadAsync(string notifiableType, int notifiableId, CancellationToken cancellationToken = default);

        /// <summary>Deletes one notification. false if it doesn't exist or belongs to someone else.</summary>
        Task<bool> DeleteAsync(string notifiableType, int notifiableId, int notificationId, CancellationToken cancellationToken = default);
    }
}

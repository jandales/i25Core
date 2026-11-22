using System.ComponentModel.DataAnnotations;
using i25Core.Core.Auditing;

namespace i25Core.Core.Entities
{
    /// <summary>
    /// One stored notification (database channel). Read and managed through INotificationInbox.
    /// Excluded from the audit log: notifications are messages, not business data.
    /// </summary>
    [AuditIgnore]
    public class DatabaseNotification : BaseEntity
    {
        /// <summary>The Notification class name, e.g. "VendorApprovedNotification".</summary>
        [MaxLength(200)]
        public string? Type { get; set; }

        /// <summary>The receiver's type, e.g. "Vendor" (INotifiable.NotifiableType).</summary>
        [MaxLength(100)]
        public string NotifiableType { get; set; } = string.Empty;

        public int NotifiableId { get; set; }

        /// <summary>JSON from Notification.ToDatabase.</summary>
        public string Data { get; set; } = "{}";

        /// <summary>null while unread.</summary>
        public DateTime? ReadAt { get; set; }
    }
}

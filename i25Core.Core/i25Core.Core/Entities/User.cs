using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using i25Core.Core.Auditing;
using i25Core.Core.Notifications;

namespace i25Core.Core.Entities
{
    /// <summary>
    /// A person who can sign in. Never return this class from an API: use <c>UserProfile</c>.
    /// The password is stored only as a salted hash, and the hash is kept out of audit logs and JSON.
    /// A User can receive notifications (INotifiable).
    /// </summary>
    public class User : BaseEntity, INotifiable
    {
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        /// <summary>Upper-cased email, used for lookups so "Ana@x.com" and "ana@x.com" are the same account.</summary>
        [MaxLength(256)]
        public string NormalizedEmail { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [AuditIgnore]
        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>An inactive user cannot sign in, and their refresh tokens stop working.</summary>
        public bool IsActive { get; set; } = true;

        public int FailedLoginCount { get; set; }

        /// <summary>While in the future, sign-in is refused (after too many wrong passwords).</summary>
        public DateTime? LockoutEnd { get; set; }

        public DateTime? LastLoginAt { get; set; }

        [JsonIgnore]
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

        // INotifiable: get-only, so EF Core does not map them.
        [JsonIgnore] public int NotifiableId => id;
        [JsonIgnore] public string? RouteNotificationForMail => Email;
    }
}

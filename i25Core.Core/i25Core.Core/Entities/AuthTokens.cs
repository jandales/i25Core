using System.ComponentModel.DataAnnotations;
using i25Core.Core.Auditing;
using i25Core.Core.Events;

namespace i25Core.Core.Entities
{
    /// <summary>
    /// A long-lived token that can be exchanged for a new access token. Only a hash of the token is stored,
    /// so a database leak does not reveal usable tokens. Excluded from the audit log and from model events.
    /// </summary>
    [AuditIgnore]
    [NoModelEvents]
    public class RefreshToken : BaseEntity
    {
        public int UserId { get; set; }

        /// <summary>SHA-256 of the token, as 64 hex characters.</summary>
        [MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>All tokens that descend from one sign-in share a family. Reusing an old one revokes the whole family.</summary>
        public Guid FamilyId { get; set; }

        public DateTime ExpiresAt { get; set; }

        [MaxLength(64)]
        public string? CreatedByIp { get; set; }

        /// <summary>Set when the token was exchanged for a new one (normal rotation).</summary>
        public DateTime? UsedAt { get; set; }

        /// <summary>Set on logout, or when reuse of an old token revoked the family.</summary>
        public DateTime? RevokedAt { get; set; }

        public bool IsActive(DateTime now) => UsedAt == null && RevokedAt == null && ExpiresAt > now;
    }

    /// <summary>A one-time token for resetting a forgotten password. Only a hash is stored.</summary>
    [AuditIgnore]
    [NoModelEvents]
    public class PasswordResetToken : BaseEntity
    {
        public int UserId { get; set; }

        [MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public DateTime? UsedAt { get; set; }
    }
}

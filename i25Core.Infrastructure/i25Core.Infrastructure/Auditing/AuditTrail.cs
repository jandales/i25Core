using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using i25Core.Core.Auditing;
using i25Core.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace i25Core.Infrastructure.Auditing
{
    /// <summary>
    /// Turns pending ChangeTracker entries into AuditLog rows.
    /// Capture() runs before SaveChanges (original values still known);
    /// Complete() runs after it (database-generated ids are known).
    /// </summary>
    internal static class AuditTrail
    {
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string Deleted = "Deleted";

        // Housekeeping columns already tracked on every row; logging them is noise.
        private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
        {
            nameof(BaseEntity.CreateAt),
            nameof(BaseEntity.ModifiedAt),
            nameof(BaseEntity.intConcurrencyId)
        };

        public static List<PendingAudit> Capture(ChangeTracker changeTracker)
        {
            changeTracker.DetectChanges();

            var pending = new List<PendingAudit>();

            foreach (var entry in changeTracker.Entries())
            {
                if (!IsAudited(entry))
                    continue;

                var action = entry.State switch
                {
                    EntityState.Added => Created,
                    EntityState.Modified => Updated,
                    EntityState.Deleted => Deleted,
                    _ => null
                };

                if (action == null)
                    continue;

                var changes = CaptureChanges(entry, action);

                // An update that only touched housekeeping columns isn't worth a log row.
                if (action == Updated && changes.Count == 0)
                    continue;

                pending.Add(new PendingAudit(entry, action, changes));
            }

            return pending;
        }

        public static List<AuditLog> Complete(IEnumerable<PendingAudit> pending, DateTime changedAtUtc)
        {
            return pending.Select(p => new AuditLog
            {
                EntityType = p.Entry.Metadata.ClrType.Name,
                EntityId = ReadEntityId(p.Entry),
                Action = p.Action,
                ChangedAt = changedAtUtc,
                ChangedBy = 0,          // no authenticated user yet
                ChangedByName = null,
                CreateAt = changedAtUtc,
                ModifiedAt = changedAtUtc,
                AuditLogChanges = new Collection<AuditLogChange>(p.Changes)
            }).ToList();
        }

        private static bool IsAudited(EntityEntry entry)
        {
            if (entry.Entity is not BaseEntity)
                return false;

            // Never audit the audit tables themselves.
            if (entry.Entity is AuditLog)
                return false;

            return entry.Metadata.ClrType.GetCustomAttribute<AuditIgnoreAttribute>() == null;
        }

        private static List<AuditLogChange> CaptureChanges(EntityEntry entry, string action)
        {
            var changes = new List<AuditLogChange>();

            foreach (var property in entry.Properties)
            {
                var metadata = property.Metadata;

                if (metadata.IsPrimaryKey() || IgnoredProperties.Contains(metadata.Name))
                    continue;

                if (metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() != null)
                    continue;

                string? original = null;
                string? current = null;

                switch (action)
                {
                    case Created:
                        current = Format(property.CurrentValue);
                        if (current == null) continue;      // skip empty fields on insert
                        break;

                    case Updated:
                        if (!property.IsModified) continue;
                        original = Format(property.OriginalValue);
                        current = Format(property.CurrentValue);
                        if (original == current) continue;  // marked modified but value unchanged
                        break;

                    case Deleted:
                        original = Format(property.OriginalValue);
                        if (original == null) continue;
                        break;
                }

                changes.Add(new AuditLogChange
                {
                    ChangeType = action,
                    PropertyName = metadata.Name,
                    OriginalValue = original,
                    NewValue = current
                });
            }

            return changes;
        }

        private static int ReadEntityId(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();

            // Single int key (BaseEntity.id); other key shapes are stored as 0.
            if (key?.Properties.Count == 1 &&
                entry.Property(key.Properties[0].Name).CurrentValue is int id)
            {
                return id;
            }

            return 0;
        }

        private static string? Format(object? value) => value switch
        {
            null => null,
            DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("o", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    internal sealed record PendingAudit(EntityEntry Entry, string Action, List<AuditLogChange> Changes);
}

using System.Text.Json;
using i25Core.Core.Entities;
using i25Core.Core.Notifications;
using Microsoft.EntityFrameworkCore;

namespace i25Core.Infrastructure.Notifications
{
    public class NotificationInbox : INotificationInbox
    {
        private readonly I25DbContext _db;

        public NotificationInbox(I25DbContext db)
        {
            _db = db;
        }

        private IQueryable<DatabaseNotification> Mine(string type, int id) =>
            _db.Set<DatabaseNotification>()
                .Where(n => n.NotifiableType == type && n.NotifiableId == id);

        public async Task<List<NotificationDto>> GetAsync(
            string notifiableType, int notifiableId, bool unreadOnly = false, int take = 50, CancellationToken cancellationToken = default)
        {
            var query = Mine(notifiableType, notifiableId);

            if (unreadOnly)
                query = query.Where(n => n.ReadAt == null);

            var rows = await query
                .AsNoTracking()
                .OrderByDescending(n => n.id)               // newest first
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync(cancellationToken);

            return rows.Select(ToDto).ToList();
        }

        public Task<int> UnreadCountAsync(string notifiableType, int notifiableId, CancellationToken cancellationToken = default) =>
            Mine(notifiableType, notifiableId).CountAsync(n => n.ReadAt == null, cancellationToken);

        public async Task<bool> MarkAsReadAsync(string notifiableType, int notifiableId, int notificationId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // Already-read rows keep their original ReadAt, so only unread ones are updated.
            var changed = await Mine(notifiableType, notifiableId)
                .Where(n => n.id == notificationId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);

            if (changed > 0)
                return true;

            // Nothing updated: either it was already read (still a success) or it isn't theirs.
            return await Mine(notifiableType, notifiableId).AnyAsync(n => n.id == notificationId, cancellationToken);
        }

        public Task<int> MarkAllAsReadAsync(string notifiableType, int notifiableId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            return Mine(notifiableType, notifiableId)
                .Where(n => n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
        }

        public async Task<bool> DeleteAsync(string notifiableType, int notifiableId, int notificationId, CancellationToken cancellationToken = default)
        {
            var deleted = await Mine(notifiableType, notifiableId)
                .Where(n => n.id == notificationId)
                .ExecuteDeleteAsync(cancellationToken);

            return deleted > 0;
        }

        private static NotificationDto ToDto(DatabaseNotification n)
        {
            JsonElement? data = null;

            try
            {
                data = JsonDocument.Parse(n.Data).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Corrupt row: return it without data rather than failing the whole inbox.
            }

            return new NotificationDto
            {
                Id = n.id,
                Type = n.Type,
                Data = data,
                ReadAt = n.ReadAt,
                CreatedAt = n.CreateAt
            };
        }
    }
}

using i25Core.BusinessLayer.Implementation.Base;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.Infrastructure;
using i25Core.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace i25Core.BusinessLayer.Implementation
{
    public class AuditLogBusinessLayerLayer : BaseBusinessLayer<AuditLog>, IAuditLogBusinessLayer
    {
        public AuditLogBusinessLayerLayer(IRepository context) : base(context)
        {
        }

        public async Task<List<AuditLog>> GetAuditLogByEntity(string entityType, int entityId)
        {
            return await _dbSet
                .AsNoTracking()
                .Include(x => x.AuditLogChanges)
                .Where(x =>
                    x.EntityType == entityType &&
                    x.EntityId == entityId)
                .OrderByDescending(x => x.ChangedAt)
                .ToListAsync();
        }
    }
}

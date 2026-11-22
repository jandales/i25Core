
using i25Core.Core.Entities;


namespace i25Core.BusinessLayer.Implementation
{
    public interface IAuditLogBusinessLayer : IBusinessLayer<AuditLog>
    {   
        Task<List<AuditLog>> GetAuditLogByEntity(string entityType, int entityId);
    }
}

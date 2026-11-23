using i25Core.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using i25Core.BusinessLayer.Implementation;

namespace i25Core.AspNetCore.Controllers
{
      [Microsoft.AspNetCore.Authorization.Authorize(Roles = i25Core.Core.Auth.Roles.Admin)]   // the audit trail is for administrators
      public class AuditLogContoller : BaseController<AuditLog>
    {
        private readonly IAuditLogBusinessLayer _bl;
        public AuditLogContoller(IAuditLogBusinessLayer bl) : base(bl)
        {
            _bl = bl;
        }

        [HttpGet("{entityType}/{entityId:int}")]
        public async Task<IActionResult> GetAuditLogByEntity(string entityType, int entityId)
        {
            var entity = await _bl.GetAuditLogByEntity(entityType, entityId);
            return OkResponse(entity);
        }



    }
}

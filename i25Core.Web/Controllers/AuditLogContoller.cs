using i25Core.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using i25Core.BusinessLayer.Implementation;

namespace i25Core.Web.Controllers
{
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

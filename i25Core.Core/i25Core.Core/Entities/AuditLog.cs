using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace i25Core.Core.Entities
{
    public class AuditLog  : BaseEntity
    {
        [Key]
        public int AuditLogId { get; set; }
        public string? EntityType {  get; set; }
        public int EntityId { get; set; }

        /// <summary>Created, Updated or Deleted.</summary>
        [MaxLength(20)]
        public string? Action { get; set; }
        public DateTime ChangedAt { get; set; }
        public int ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
        public string? Remarks { get; set; }

        public Collection<AuditLogChange>? AuditLogChanges { get; set; }

    }
}

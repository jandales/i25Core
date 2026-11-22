using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace i25Core.Core.Entities
{
    public class AuditLogChange
    {
        [Key]
        public int AuditLogChangeId { get; set; }
        [ForeignKey(nameof(AuditLogChangeId))]
        public int AuditLogId { get; set; }
        public string? ChangeType { get; set; }
        public string? OriginalValue { get; set; }
        public string? NewValue { get; set; }

        [JsonIgnore]
        public AuditLog? AuditLog { get; set; }

    }
}

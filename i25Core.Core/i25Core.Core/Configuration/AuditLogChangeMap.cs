using i25Core.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace i25Core.Core.Configuration
{
    public class AuditLogChangeMap : IEntityTypeConfiguration<AuditLogChange>
    {
        public void Configure(EntityTypeBuilder<AuditLogChange> builder)
        {
            builder.ToTable("AuditLogChange");

        }
    }
}

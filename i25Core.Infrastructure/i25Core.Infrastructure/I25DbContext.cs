using Microsoft.EntityFrameworkCore;
using i25Core.Core.Entities;
using i25Core.Core.Configuration;


namespace i25Core.Infrastructure
{
    public class I25DbContext : DbContext
    {
        public I25DbContext(DbContextOptions<I25DbContext> options) : base(options) { }

        public DbSet<Category> Category { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<AuditLogChange> AuditLogChange { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new AuditLogMap());
            modelBuilder.ApplyConfiguration(new AuditLogChangeMap());

            DbSeed.Apply(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(nameof(BaseEntity.intConcurrencyId))
                        .IsConcurrencyToken();
                }
            }      
        }

        public override int SaveChanges()
        {
            IncrementConcurrencyIds();
            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            IncrementConcurrencyIds();

            var entries = ChangeTracker
                           .Entries()
                           .Where(e => e.Entity is BaseEntity &&
                                       (e.State == EntityState.Added ||
                                        e.State == EntityState.Modified));

            foreach (var entry in entries)
            {
                var entity = (BaseEntity)entry.Entity;

                if (entry.State == EntityState.Added)
                {
                    entity.CreateAt = DateTime.UtcNow;
                    entity.ModifiedAt = DateTime.UtcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entity.ModifiedAt = DateTime.UtcNow;

                    // Don't change CreateAt during an update
                    entry.Property(nameof(BaseEntity.CreateAt)).IsModified = false;
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        private void IncrementConcurrencyIds()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>().Where(e => e.State == EntityState.Modified))
            {
                entry.Entity.intConcurrencyId = (entry.Entity.intConcurrencyId ?? 0) + 1;
            }
        }
    }
}

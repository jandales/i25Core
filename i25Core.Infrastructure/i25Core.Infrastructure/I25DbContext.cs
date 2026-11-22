using Microsoft.EntityFrameworkCore;
using i25Core.Core.Entities;
using i25Core.Core.Configuration;
using i25Core.Infrastructure.Auditing;


namespace i25Core.Infrastructure
{
    public class I25DbContext : DbContext
    {
        public I25DbContext(DbContextOptions<I25DbContext> options) : base(options) { }

        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<AuditLogChange> AuditLogChange { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new AuditLogMap());
            modelBuilder.ApplyConfiguration(new AuditLogChangeMap());
            modelBuilder.ApplyConfiguration(new DatabaseNotificationMap());

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

            var pending = AuditTrail.Capture(ChangeTracker);

            if (pending.Count == 0)
                return base.SaveChanges();

            // Data change and its audit rows commit or roll back together.
            using var transaction = Database.CurrentTransaction == null
                ? Database.BeginTransaction()
                : null;

            var result = base.SaveChanges();

            AuditLogs.AddRange(AuditTrail.Complete(pending, DateTime.UtcNow));
            base.SaveChanges();

            transaction?.Commit();

            return result;
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

            var pending = AuditTrail.Capture(ChangeTracker);

            if (pending.Count == 0)
                return await base.SaveChangesAsync(cancellationToken);

            // Data change and its audit rows commit or roll back together.
            await using var transaction = Database.CurrentTransaction == null
                ? await Database.BeginTransactionAsync(cancellationToken)
                : null;

            // First save: writes the data and fills database-generated ids for inserts.
            var result = await base.SaveChangesAsync(cancellationToken);

            // Second save: writes the audit rows (base call, so they are not audited themselves).
            AuditLogs.AddRange(AuditTrail.Complete(pending, DateTime.UtcNow));
            await base.SaveChangesAsync(cancellationToken);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);

            return result;
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

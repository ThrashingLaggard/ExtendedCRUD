using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CRUD_.Tests
{
    public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
        public DbSet<SoftDeletableTestEntity> SoftDeletableTestEntities => Set<SoftDeletableTestEntity>();
        public DbSet<RowVersionedTestEntity> RowVersionedTestEntities => Set<RowVersionedTestEntity>();
        public DbSet<FilterableTestEntity> FilterableTestEntities => Set<FilterableTestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Used to provoke a genuine DbUpdateConcurrencyException in the concurrency tests
            // instead of relying on a hand-thrown stand-in exception.
            modelBuilder.Entity<TestEntity>().Property(e => e.Name).IsConcurrencyToken();

            // [Timestamp] alone marks RowVersion as database-generated, but neither the
            // InMemory provider nor SQLite (see CRUD+ README "Concurrency") actually generate
            // a fresh value on save - confirmed here the same way it was confirmed against a
            // real SQLite database for the CRUDimplementation sample host. ValueGeneratedNever()
            // plus the manual bump in SaveChanges below is required for the token to actually
            // change between saves.
            modelBuilder.Entity<RowVersionedTestEntity>().Property(e => e.RowVersion).ValueGeneratedNever();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            BumpRowVersions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            BumpRowVersions();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void BumpRowVersions()
        {
            foreach (EntityEntry<RowVersionedTestEntity> entry in ChangeTracker.Entries<RowVersionedTestEntity>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
                }
            }
        }
    }
}

using CRUDimplementation.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CRUDimplementation
{
    /// <summary>
    /// The demo host's own <see cref="DbContext"/>, injected into CRUD+'s generic
    /// <c>CrudRepository&lt;T&gt;</c>/<c>CrudService&lt;T&gt;</c> via DI — this is the
    /// "consuming application" side of the library, not part of CRUD+ itself.
    /// </summary>
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Note> Notes => Set<Note>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Per CRUD+'s documented soft-delete pattern (README "Soft delete"): the library
            // itself never filters ISoftDeletable rows out of GetAll/Pageineering/Find - that's
            // the consuming app's responsibility, configured here.
            modelBuilder.Entity<Note>().HasQueryFilter(n => !n.IsDeleted);

            // [Timestamp] alone marks RowVersion as database-generated (ValueGeneratedOnAddOrUpdate),
            // which tells EF Core to omit it from INSERT/UPDATE statements and read the value back
            // from the store afterward - the SQL Server rowversion behavior. SQLite has no such
            // store-side generator, so with the attribute alone every insert violates the column's
            // NOT NULL constraint (confirmed by actually running this against SQLite, not assumed).
            // ValueGeneratedNever() makes EF Core send the value BumpRowVersions() assigns
            // app-side instead, while IsConcurrencyToken() (still set by [Timestamp]) is untouched.
            modelBuilder.Entity<Product>().Property(p => p.RowVersion).ValueGeneratedNever();
            modelBuilder.Entity<Note>().Property(n => n.RowVersion).ValueGeneratedNever();
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

        /// <summary>
        /// Assigns a fresh <see cref="IHasRowVersion.RowVersion"/> to every added/modified
        /// entity before the actual save. SQLite has no native rowversion column type (the
        /// mechanism <c>[Timestamp]</c> relies on for automatic generation on SQL Server), so
        /// the "new value on every write" half of the concurrency-token contract has to be done
        /// here instead. EF Core's own optimistic-concurrency check (comparing the *original*
        /// tracked value against what's actually in the database at save time) is unaffected by
        /// this — that part works the same on every provider once a token exists at all.
        /// </summary>
        private void BumpRowVersions()
        {
            foreach (EntityEntry<IHasRowVersion> entry in ChangeTracker.Entries<IHasRowVersion>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
                }
            }
        }
    }
}

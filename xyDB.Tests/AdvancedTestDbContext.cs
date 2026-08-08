using Microsoft.EntityFrameworkCore;

namespace xyDB.Tests
{
    /// <summary>
    /// Context for the 1.0.2 capabilities, exercised against real SQLite rather than the
    /// InMemory provider.
    /// </summary>
    /// <remarks>
    /// InMemory supports neither transactions nor <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, so
    /// testing the multi-phase transactional write or the set-based operations against it would
    /// prove nothing. SQLite runs real SQL and real transactions while still being a throwaway
    /// in-process database.
    /// </remarks>
    public class AdvancedTestDbContext(DbContextOptions<AdvancedTestDbContext> options) : DbContext(options)
    {
        public DbSet<NodeEntity> Nodes => Set<NodeEntity>();
        public DbSet<TagEntity> Tags => Set<TagEntity>();
        public DbSet<CompositeKeyEntity> CompositeKeyEntities => Set<CompositeKeyEntity>();
        public DbSet<KeylessEntity> KeylessEntities => Set<KeylessEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NodeEntity>(node =>
            {
                // The key is NodeId. The Id property alongside it is a non-unique business
                // identifier, indexed for lookup only - deliberately not a key and not unique.
                node.HasKey(n => n.NodeId);
                node.HasIndex(n => n.Id);

                // Soft delete expressed the temporal way: a row is live while ValidTo is null.
                node.HasQueryFilter(n => n.ValidTo == null);
            });

            modelBuilder.Entity<TagEntity>(tag => tag.HasKey(t => t.TagId));

            modelBuilder.Entity<CompositeKeyEntity>(composite => composite.HasKey(c => new { c.LeftId, c.RightId }));

            modelBuilder.Entity<KeylessEntity>(keyless => keyless.HasNoKey().ToTable("KeylessEntities"));
        }
    }
}

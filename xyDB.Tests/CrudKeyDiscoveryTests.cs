using CRUD.Repos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace xyDB.Tests
{
    /// <summary>
    /// Covers P0-2: the primary key comes from the EF Core model, never from a property named
    /// <c>Id</c>, and entity types that cannot be addressed by one key value stay usable.
    /// </summary>
    public class CrudKeyDiscoveryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AdvancedTestDbContext> _options;

        public CrudKeyDiscoveryTests()
        {
            // A shared-cache in-memory SQLite database lives as long as the connection does,
            // so every context built from these options sees the same tables.
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AdvancedTestDbContext>().UseSqlite(_connection).Options;

            using var schema = new AdvancedTestDbContext(_options);
            schema.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private AdvancedTestDbContext CreateContext() => new(_options);
        [Fact]
        public async Task DuplicateBusinessIds_AreASupportedState_NotAKeyCollision()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.AddRange(
                new NodeEntity { NodeId = 1, Id = "dup", Path = "a.md" },
                new NodeEntity { NodeId = 2, Id = "dup", Path = "b.md" });
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context);

            IEnumerable<NodeEntity> both = await repo.Find(n => n.Id == "dup");

            Assert.Equal(2, both.Count());
        }

        [Fact]
        public async Task ExistsAsync_ResolvesAgainstTheRealKey()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.Add(new NodeEntity { NodeId = 7, Id = "x", Path = "a.md" });
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context);

            Assert.True(await repo.ExistsAsync(7));
            Assert.False(await repo.ExistsAsync(8));
        }

        [Fact]
        public async Task FindByIdsAsync_ResolvesAgainstTheRealKey()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.AddRange(
                new NodeEntity { NodeId = 1, Path = "a.md" },
                new NodeEntity { NodeId = 2, Path = "b.md" },
                new NodeEntity { NodeId = 3, Path = "c.md" });
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context);

            IEnumerable<NodeEntity> found = await repo.FindByIdsAsync([1, 3, 99]);

            Assert.Equal([1, 3], found.Select(n => n.NodeId).OrderBy(id => id));
        }

        [Fact]
        public async Task Pageineering_FallsBackToTheModelsKey_ForStableOrder()
        {
            await using AdvancedTestDbContext context = CreateContext();
            for (int i = 5; i >= 1; i--)
            {
                context.Nodes.Add(new NodeEntity { NodeId = i, Path = $"{i}.md" });
            }
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context);

            IEnumerable<NodeEntity> page = await repo.Pageineering(1, 3);

            Assert.Equal([1, 2, 3], page.Select(n => n.NodeId));
        }

        [Fact]
        public void ConstructingARepository_OverACompositeKeyEntity_DoesNotThrow()
        {
            using AdvancedTestDbContext context = CreateContext();

            // Registration-time safety: these types exist in the model for later use, and
            // merely wiring a repository over them must never fail.
            CrudRepository<CompositeKeyEntity> repo = new(context);

            Assert.NotNull(repo);
        }

        [Fact]
        public void ConstructingARepository_OverAKeylessEntity_DoesNotThrow()
        {
            using AdvancedTestDbContext context = CreateContext();

            CrudRepository<KeylessEntity> repo = new(context);

            Assert.NotNull(repo);
        }

        [Fact]
        public async Task CompositeKeyEntity_StaysFullyUsableThroughNonKeyMembers()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.CompositeKeyEntities.Add(new CompositeKeyEntity { LeftId = 1, RightId = 2, Note = "pair" });
            await context.SaveChangesAsync();

            var repo = new CrudRepository<CompositeKeyEntity>(context);

            // Predicate queries and paging still work; only single-key addressing is not
            // available for this entity type.
            IEnumerable<CompositeKeyEntity> found = await repo.Find(c => c.Note == "pair");
            Assert.Single(found);
            Assert.Single(await repo.Pageineering(1, 10));
        }

        [Fact]
        public async Task Pageineering_OnKeylessEntity_ReturnsRowsInsteadOfRefusing()
        {
            await using AdvancedTestDbContext context = CreateContext();
            // Keyless types cannot be tracked, so they are seeded with SQL rather than Add().
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO KeylessEntities (Label, Count) VALUES ('a', 1), ('b', 2);");

            var repo = new CrudRepository<KeylessEntity>(context);

            // No key to order by, so the order is the provider's - but the query still runs.
            Assert.Equal(2, (await repo.Pageineering(1, 10)).Count());
        }
    }
}

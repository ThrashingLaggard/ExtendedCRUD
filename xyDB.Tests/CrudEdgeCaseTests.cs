using CRUD.Exceptions;
using CRUD.Options;
using CRUD.Repos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace xyDB.Tests
{
    /// <summary>
    /// Edge cases and pinned behaviors around the 1.0.2 surface.
    /// </summary>
    public class CrudEdgeCaseTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AdvancedTestDbContext> _options;

        public CrudEdgeCaseTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AdvancedTestDbContext>().UseSqlite(_connection).Options;

            using var schema = new AdvancedTestDbContext(_options);
            schema.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private AdvancedTestDbContext CreateContext() => new(_options);

        private static CrudSoftDeleteOptions<NodeEntity> ValidToMarker() =>
            CrudSoftDeleteOptions<NodeEntity>.ClosedByTimestamp(n => n.ValidTo);

        [Fact]
        public async Task LegacyRead_StillReturnsATrackedClosedRow_MatchingReleasedBehaviour()
        {
            // Pinned deliberately. Read(int) resolves through DbContext.FindAsync, which checks
            // the change tracker before querying, so a row closed earlier in the same context
            // comes back without the global filter being applied. This is exactly what the
            // published 1.0.1 does - verified against the released package - so changing it
            // would be the breaking change, not keeping it.
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.SoftDeleteAsync(node);

            Assert.NotNull(await repo.Read(1));
            // ...and the documented safe alternative genuinely differs.
            Assert.Null(await repo.ReadByKeyAsync(1));
        }

        [Fact]
        public async Task ReadByKeysAsync_WithGuidKeys_ReturnsOnlyTheRequestedRows()
        {
            Guid wanted = Guid.NewGuid();
            await using AdvancedTestDbContext context = CreateContext();
            context.Tags.AddRange(
                new TagEntity { TagId = wanted, NodeId = 1, Label = "a" },
                new TagEntity { TagId = Guid.NewGuid(), NodeId = 1, Label = "b" });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<TagEntity>(context);

            IReadOnlyList<TagEntity> found = await repo.ReadByKeysAsync([wanted]);

            Assert.Equal("a", Assert.Single(found).Label);
        }

        [Fact]
        public async Task ReadByKeysAsync_WithNoKeys_ReturnsEmptyWithoutQuerying()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            Assert.Empty(await repo.ReadByKeysAsync([]));
        }

        [Fact]
        public async Task ReadByKeysAsync_HonorsGlobalFilters()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.AddRange(
                new NodeEntity { NodeId = 1, Path = "live.md" },
                new NodeEntity { NodeId = 2, Path = "closed.md", ValidTo = new DateTime(2026, 1, 1) });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<NodeEntity>(context);

            IReadOnlyList<NodeEntity> found = await repo.ReadByKeysAsync([1, 2]);

            Assert.Equal(1, Assert.Single(found).NodeId);
        }

        [Fact]
        public async Task ReadByKeyAsync_WithAnUnconvertibleKeyValue_SaysSo()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            CrudKeyException error = await Assert.ThrowsAsync<CrudKeyException>(
                () => repo.ReadByKeyAsync("not-an-int"));

            Assert.Equal(CrudKeyProblem.ValueNotConvertible, error.Reason);
        }

        [Fact]
        public async Task ExecuteUpdateAsync_DoesNotReachRowsAGlobalFilterHides()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.AddRange(
                new NodeEntity { NodeId = 1, Path = "live.md" },
                new NodeEntity { NodeId = 2, Path = "closed.md", ValidTo = new DateTime(2026, 1, 1) });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<NodeEntity>(context);

            // A set-based write must not silently touch rows an ordinary read cannot see.
            int updated = await repo.ExecuteUpdateAsync(
                _ => true,
                setters => setters.SetProperty(n => n.ParseError, "touched"));

            Assert.Equal(1, updated);
            NodeEntity closed = (await repo.ReadByKeyIncludingFilteredAsync(2))!;
            Assert.Null(closed.ParseError);
        }

        [Fact]
        public async Task SoftDeleteRangeNoSaveAsync_ClosesEveryEntityInOneFlush()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var nodes = new List<NodeEntity>
            {
                new() { NodeId = 1, Path = "a.md" },
                new() { NodeId = 2, Path = "b.md" },
            };
            context.Nodes.AddRange(nodes);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());

            await repo.SoftDeleteRangeNoSaveAsync(nodes);
            Assert.Equal(2, await repo.Query(ignoreQueryFilters: true).CountAsync(n => n.ValidTo == null));

            await repo.SaveChangesAsync();
            Assert.Equal(0, await repo.Query().CountAsync());
        }

        [Fact]
        public async Task SoftDelete_LeavesUnrelatedPendingChangesUntouched()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md", ParseError = "original" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.SoftDeleteAsync(node);

            // Only the marker column was written, so the other value survives as it was.
            NodeEntity closed = (await repo.ReadByKeyIncludingFilteredAsync(1))!;
            Assert.Equal("original", closed.ParseError);
        }

        [Fact]
        public void SoftDeleteOptions_RejectANonPropertySelector()
        {
            Assert.Throws<CrudConfigurationException>(
                () => CrudSoftDeleteOptions<NodeEntity>.MarkedBy(n => n.Path.Length, () => 0, () => 0));
        }

        [Fact]
        public async Task SoftDelete_WithAMarkerThatIsNotMapped_SaysSo()
        {
            await using AdvancedTestDbContext context = CreateContext();
            // KeylessEntity has no mapped NodeEntity marker; use a genuinely unmapped property.
            var options = CrudSoftDeleteOptions<NodeEntity>.MarkedBy(n => n.Unmapped, () => true, () => false);
            var repo = new CrudRepository<NodeEntity>(context, options);

            await Assert.ThrowsAsync<CrudConfigurationException>(
                () => repo.SoftDeleteAsync(new NodeEntity { NodeId = 1, Path = "a.md" }));
        }

        [Fact]
        public async Task HardDeleteRangeAsync_RemovesEveryRow()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var nodes = new List<NodeEntity>
            {
                new() { NodeId = 1, Path = "a.md" },
                new() { NodeId = 2, Path = "b.md" },
            };
            context.Nodes.AddRange(nodes);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());

            Assert.Equal(2, await repo.HardDeleteRangeAsync(nodes));
            Assert.Equal(0, await repo.Query(ignoreQueryFilters: true).CountAsync());
        }

        [Fact]
        public async Task RemoveRangeNoSaveAsync_StagesWithoutSaving()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var nodes = new List<NodeEntity> { new() { NodeId = 1, Path = "a.md" } };
            context.Nodes.AddRange(nodes);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context);

            await repo.RemoveRangeNoSaveAsync(nodes);
            await using (AdvancedTestDbContext observer = CreateContext())
            {
                Assert.Equal(1, await observer.Nodes.CountAsync());
            }

            await repo.SaveChangesAsync();
            await using (AdvancedTestDbContext observer = CreateContext())
            {
                Assert.Equal(0, await observer.Nodes.CountAsync());
            }
        }

        [Fact]
        public async Task Pageineering_OnACompositeKeyEntity_OrdersByEveryKeyComponent()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.CompositeKeyEntities.AddRange(
                new CompositeKeyEntity { LeftId = 2, RightId = 1, Note = "c" },
                new CompositeKeyEntity { LeftId = 1, RightId = 2, Note = "b" },
                new CompositeKeyEntity { LeftId = 1, RightId = 1, Note = "a" });
            await context.SaveChangesAsync();

            var repo = new CrudRepository<CompositeKeyEntity>(context);

            IEnumerable<CompositeKeyEntity> page = await repo.Pageineering(1, 10);

            Assert.Equal(["a", "b", "c"], page.Select(c => c.Note));
        }

        [Fact]
        public async Task NullArguments_AreRejectedByTheAsyncSurface()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddRangeNoSaveAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpdateRangeNoSaveAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.RemoveNoSaveAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.ReadByKeyAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.ExecuteDeleteAsync(null!));
        }
    }
}

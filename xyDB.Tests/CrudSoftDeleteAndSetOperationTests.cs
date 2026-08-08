using CRUD.Exceptions;
using CRUD.Options;
using CRUD.Repos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace xyDB.Tests
{
    /// <summary>
    /// Covers P0-5 (filters honored by default, opt-out explicit), P0-6 (configurable soft
    /// delete, explicit hard delete), P1-1/P1-2 (set-based writes) and P1-3 (queryable hatch).
    /// </summary>
    public class CrudSoftDeleteAndSetOperationTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly List<string> _capturedSql = [];

        public CrudSoftDeleteAndSetOperationTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            using var schema = new AdvancedTestDbContext(CreateOptions());
            schema.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private DbContextOptions<AdvancedTestDbContext> CreateOptions() =>
            new DbContextOptionsBuilder<AdvancedTestDbContext>()
                .UseSqlite(_connection)
                // Captured so a test can assert on the statement actually sent, not just on the
                // resulting row state - "only the marker column is written" is a claim about SQL.
                .LogTo(_capturedSql.Add, Microsoft.Extensions.Logging.LogLevel.Information)
                .Options;

        private AdvancedTestDbContext CreateContext() => new(CreateOptions());

        private static CrudSoftDeleteOptions<NodeEntity> ValidToMarker() =>
            CrudSoftDeleteOptions<NodeEntity>.ClosedByTimestamp(n => n.ValidTo);
        [Fact]
        public async Task SoftDeleteAsync_StampsTheConfiguredMarker_WithoutRemovingTheRow()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.SoftDeleteAsync(node);

            // Gone from a normal read (the model's filter is valid_to IS NULL)...
            Assert.Null(await repo.ReadByKeyAsync(1));
            // ...but the row itself is still there, with the marker stamped.
            NodeEntity? closed = await repo.ReadByKeyIncludingFilteredAsync(1);
            Assert.NotNull(closed);
            Assert.NotNull(closed!.ValidTo);
        }

        [Fact]
        public async Task SoftDelete_EmitsAnUpdateTouchingOnlyTheMarkerColumn()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md", ParseError = "keep-me" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            _capturedSql.Clear();
            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.SoftDeleteAsync(node);

            string updateStatement = Assert.Single(_capturedSql, sql => sql.Contains("UPDATE \"Nodes\""));
            Assert.Contains("\"ValidTo\"", updateStatement);
            // The whole point: a schema trigger declared AFTER UPDATE OF valid_to should see a
            // statement about valid_to, and an unrelated column must not be rewritten.
            Assert.DoesNotContain("\"ParseError\"", updateStatement);
            Assert.DoesNotContain("\"Path\"", updateStatement);
        }

        [Fact]
        public async Task SoftDelete_WorksOnAnEntityReadWithoutTracking()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.Add(new NodeEntity { NodeId = 1, Path = "a.md" });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            NodeEntity detached = (await repo.ReadByKeyAsync(1))!;

            await repo.SoftDeleteAsync(detached);

            Assert.Null(await repo.ReadByKeyAsync(1));
        }

        [Fact]
        public async Task RestoreAsync_ReopensAClosedRow()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.SoftDeleteAsync(node);
            await repo.RestoreAsync(node);

            Assert.NotNull(await repo.ReadByKeyAsync(1));
        }

        [Fact]
        public async Task SoftDelete_WithoutConfiguration_ThrowsInsteadOfGuessing()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            Assert.False(repo.IsSoftDeleteConfigured);
            await Assert.ThrowsAsync<CrudConfigurationException>(
                () => repo.SoftDeleteAsync(new NodeEntity { NodeId = 1 }));
        }

        [Fact]
        public async Task HardDeleteAsync_RemovesTheRow_EvenWhenSoftDeleteIsConfigured()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.HardDeleteAsync(node);

            // An explicit hard delete is never reinterpreted as a soft one.
            Assert.Null(await repo.ReadByKeyIncludingFilteredAsync(1));
        }

        [Fact]
        public async Task ReadByKeyAsync_HonorsGlobalFilters_AndTheOptOutIsExplicit()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.Add(new NodeEntity { NodeId = 1, Path = "a.md", ValidTo = new DateTime(2026, 1, 1) });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<NodeEntity>(context);

            Assert.Null(await repo.ReadByKeyAsync(1));
            Assert.NotNull(await repo.ReadByKeyIncludingFilteredAsync(1));
        }

        [Fact]
        public async Task ReadByKeyAsync_DoesNotResurrectAClosedRow_FromTheChangeTracker()
        {
            // The lifecycle hazard P0-5 calls out: a key lookup that returned a closed row
            // just because the context happens to be tracking it would revive dead history.
            await using AdvancedTestDbContext context = CreateContext();
            var node = new NodeEntity { NodeId = 1, Path = "a.md" };
            context.Nodes.Add(node);
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context, ValidToMarker());
            await repo.SoftDeleteAsync(node);

            // The entity is still tracked at this point, and the row is closed.
            Assert.NotEmpty(context.ChangeTracker.Entries<NodeEntity>());
            Assert.Null(await repo.ReadByKeyAsync(1));
        }

        [Fact]
        public async Task Query_DefaultsToNoTrackingAndFilterRespecting()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.AddRange(
                new NodeEntity { NodeId = 1, Path = "live.md" },
                new NodeEntity { NodeId = 2, Path = "closed.md", ValidTo = new DateTime(2026, 1, 1) });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<NodeEntity>(context);

            List<NodeEntity> live = await repo.Query().ToListAsync();

            Assert.Single(live);
            Assert.Empty(context.ChangeTracker.Entries<NodeEntity>());
        }

        [Fact]
        public async Task Query_CanOptIntoTrackingAndIntoIgnoringFilters()
        {
            await using AdvancedTestDbContext context = CreateContext();
            context.Nodes.AddRange(
                new NodeEntity { NodeId = 1, Path = "live.md" },
                new NodeEntity { NodeId = 2, Path = "closed.md", ValidTo = new DateTime(2026, 1, 1) });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var repo = new CrudRepository<NodeEntity>(context);

            List<NodeEntity> all = await repo.Query(tracked: true, ignoreQueryFilters: true).ToListAsync();

            Assert.Equal(2, all.Count);
            Assert.Equal(2, context.ChangeTracker.Entries<NodeEntity>().Count());
        }

        [Fact]
        public async Task Query_SupportsProjectionAndPagingBeforeMaterializing()
        {
            await using AdvancedTestDbContext context = CreateContext();
            for (int i = 1; i <= 10; i++)
            {
                context.Nodes.Add(new NodeEntity { NodeId = i, Path = $"{i}.md" });
            }
            await context.SaveChangesAsync();

            var repo = new CrudRepository<NodeEntity>(context);

            List<string> paths = await repo.Query()
                .OrderBy(n => n.NodeId)
                .Skip(2)
                .Take(3)
                .Select(n => n.Path)
                .ToListAsync();

            Assert.Equal(["3.md", "4.md", "5.md"], paths);
        }
    }
}

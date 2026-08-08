using CRUD.Repos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace xyDB.Tests
{
    /// <summary>
    /// Covers P0-3: the caller owns the context, the transaction and every flush.
    /// </summary>
    /// <remarks>
    /// Run against SQLite because the point being proven is transactional — the InMemory
    /// provider has no transactions and would silently pass tests that mean nothing.
    /// </remarks>
    public class CrudTransactionalWriteTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AdvancedTestDbContext> _options;

        public CrudTransactionalWriteTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AdvancedTestDbContext>().UseSqlite(_connection).Options;

            using var schema = new AdvancedTestDbContext(_options);
            schema.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private AdvancedTestDbContext CreateContext() => new(_options);

        [Fact]
        public async Task StagingMembers_DoNotSave_UntilTheCallerSaves()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            await repo.AddNoSaveAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
            await repo.AddRangeNoSaveAsync([new NodeEntity { NodeId = 2, Path = "b.md" }]);

            // Nothing has reached the database yet; the change tracker holds it all.
            await using (AdvancedTestDbContext observer = CreateContext())
            {
                Assert.Equal(0, await observer.Nodes.CountAsync());
            }

            await repo.SaveChangesAsync();

            await using (AdvancedTestDbContext observer = CreateContext())
            {
                Assert.Equal(2, await observer.Nodes.CountAsync());
            }
        }

        [Fact]
        public async Task StagedWork_RollsBackWithTheCallersTransaction()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync())
            {
                await repo.AddNoSaveAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
                await repo.SaveChangesAsync();

                // The save flushed, but nothing committed it - so rolling back must discard it.
                await transaction.RollbackAsync();
            }

            await using AdvancedTestDbContext observer = CreateContext();
            Assert.Equal(0, await observer.Nodes.CountAsync());
        }

        [Fact]
        public async Task SaveChangesAsync_DoesNotCommitTheCallersTransaction()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
            await repo.AddNoSaveAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
            await repo.SaveChangesAsync();

            // If any library call had committed implicitly, the transaction would no longer be
            // the context's current one.
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Equal(transaction.TransactionId, context.Database.CurrentTransaction!.TransactionId);

            await transaction.CommitAsync();
            await using AdvancedTestDbContext observer = CreateContext();
            Assert.Equal(1, await observer.Nodes.CountAsync());
        }

        [Fact]
        public async Task SavingMembers_EnlistInTheCallersTransaction_RatherThanOpeningTheirOwn()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync())
            {
                // The immediately-saving members must enlist too, not open a nested/independent
                // transaction that would commit behind the caller's back.
                await repo.CreateAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
                await repo.Create(new NodeEntity { NodeId = 2, Path = "b.md" });

                await transaction.RollbackAsync();
            }

            await using AdvancedTestDbContext observer = CreateContext();
            Assert.Equal(0, await observer.Nodes.CountAsync());
        }

        [Fact]
        public async Task ThreePhaseWrite_WithFlushBoundaries_SatisfiesAFilteredUniqueIndex()
        {
            // The scenario the no-save API exists for. A filtered unique index over live rows
            // means the intermediate states of a reordering write are illegal, so the batch has
            // to flush at points only the caller knows about - all inside one transaction.
            await using (AdvancedTestDbContext setup = CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE sections (section_id INTEGER PRIMARY KEY, node_id INTEGER NOT NULL, " +
                    "section_index INTEGER NOT NULL, valid_to TEXT NULL);");
                await setup.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX ux_sections_node_index_live ON sections (node_id, section_index) WHERE valid_to IS NULL;");
                await setup.Database.ExecuteSqlRawAsync(
                    "INSERT INTO sections (section_id, node_id, section_index, valid_to) VALUES (1, 1, 0, NULL), (2, 1, 1, NULL);");
            }

            await using AdvancedTestDbContext context = CreateContext();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            // Phase 1: close the row being replaced.
            await context.Database.ExecuteSqlRawAsync("UPDATE sections SET valid_to = '2026-01-01' WHERE section_id = 1;");

            // Phase 2: park the surviving row at a negative sentinel so its target index frees up.
            await context.Database.ExecuteSqlRawAsync("UPDATE sections SET section_index = -section_id WHERE section_id = 2;");

            // Phase 3: write final positions. Without the phase 2 boundary this collides.
            await context.Database.ExecuteSqlRawAsync("UPDATE sections SET section_index = 0 WHERE section_id = 2;");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO sections (section_id, node_id, section_index, valid_to) VALUES (3, 1, 1, NULL);");

            await transaction.CommitAsync();

            await using AdvancedTestDbContext observer = CreateContext();
            int liveRows = 0;
            await using (SqliteCommand command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sections WHERE valid_to IS NULL;";
                liveRows = Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            Assert.Equal(2, liveRows);
        }

        [Fact]
        public async Task RepositoryNeverDisposesTheCallersContext()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            await repo.CreateAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
            await repo.HardDeleteAsync(await repo.ReadByKeyAsync(1, tracked: true) ?? throw new InvalidOperationException());

            // Still usable afterwards: the repository owns nothing about this context's lifetime.
            Assert.Equal(0, await context.Nodes.CountAsync());
        }

        [Fact]
        public async Task StagingRangeMembers_AreNotSubjectToTheBulkBatchCap()
        {
            // The cap bounds a single database round-trip. Staging performs none, so a batch
            // larger than the cap must stage rather than be silently rejected.
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            List<NodeEntity> oversized = Enumerable.Range(1, 501)
                .Select(i => new NodeEntity { NodeId = i, Path = $"{i}.md" })
                .ToList();

            await repo.AddRangeNoSaveAsync(oversized);
            await repo.SaveChangesAsync();

            Assert.Equal(501, await context.Nodes.CountAsync());
        }
    }
}

using CRUD.Interfaces;
using CRUD.Repos;
using CRUD.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace xyDB.Tests
{
    /// <summary>
    /// Covers P0-4 and P1-4: the <c>Async</c> members take a cancellation token and report
    /// failure by throwing, while the original members keep their existing contract.
    /// </summary>
    public class CrudAsyncContractTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AdvancedTestDbContext> _options;

        public CrudAsyncContractTests()
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
        public async Task CreateAsync_WithNullEntity_Throws_WhereLegacyCreateReturnsFalse()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            // The new contract surfaces the problem...
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.CreateAsync(null!));

            // ...while the original one still answers with a defined false, unchanged.
            Assert.False(await repo.Create(null!));
        }

        [Fact]
        public async Task CreateAsync_ReturnsTheNumberOfWrittenEntries()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            int written = await repo.CreateAsync(new NodeEntity { NodeId = 1, Path = "a.md" });

            Assert.Equal(1, written);
        }

        [Fact]
        public async Task CreateAsync_LetsAFailedWritePropagate_RatherThanSwallowingIt()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);
            await repo.CreateAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
            context.ChangeTracker.Clear();

            // Duplicate primary key: the database rejects it, and that must reach the caller
            // instead of being flattened into a silent no-op.
            await Assert.ThrowsAsync<DbUpdateException>(
                () => repo.CreateAsync(new NodeEntity { NodeId = 1, Path = "b.md" }));
        }

        [Fact]
        public async Task AsyncMembers_ObserveACancelledToken()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => repo.CreateAsync(new NodeEntity { NodeId = 1, Path = "a.md" }, cancelled.Token));
        }

        [Fact]
        public async Task StagingMembers_ObserveACancelledToken()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var repo = new CrudRepository<NodeEntity>(context);

            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => repo.UpdateNoSaveAsync(new NodeEntity { NodeId = 1 }, cancelled.Token));
        }

        [Fact]
        public async Task ServiceLayer_ExposesTheNewSurface_AndPassesExceptionsThrough()
        {
            await using AdvancedTestDbContext context = CreateContext();
            var service = new CrudService<NodeEntity>(new CrudRepository<NodeEntity>(context));

            await service.AddNoSaveAsync(new NodeEntity { NodeId = 1, Path = "a.md" });
            Assert.Equal(0, await context.Nodes.CountAsync());

            Assert.Equal(1, await service.SaveChangesAsync());
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
        }

        [Fact]
        public void ExistingConsumers_CanStillDependOnIExtendedCrud()
        {
            using AdvancedTestDbContext context = CreateContext();

            // The published interface is unchanged, so code written against it still binds.
            IExtendedCrud<NodeEntity> asExtended = new CrudRepository<NodeEntity>(context);
            IExtendedCrud<NodeEntity> serviceAsExtended = new CrudService<NodeEntity>(new CrudRepository<NodeEntity>(context));

            Assert.NotNull(asExtended);
            Assert.NotNull(serviceAsExtended);
        }

        [Fact]
        public void TheNewSurface_IsReachableThroughIAdvancedCrud()
        {
            using AdvancedTestDbContext context = CreateContext();

            IAdvancedCrud<NodeEntity> repo = new CrudRepository<NodeEntity>(context);
            IAdvancedCrud<NodeEntity> service = new CrudService<NodeEntity>(new CrudRepository<NodeEntity>(context));

            Assert.NotNull(repo.Query());
            Assert.NotNull(service.Query());
        }
    }
}

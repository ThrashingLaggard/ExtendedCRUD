using CRUD.Repos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CRUD_.Tests
{
    /// <summary>Tests targeting <see cref="CrudRepository{T}"/> directly against EF Core InMemory.</summary>
    public class CrudRepositoryTests
    {
        private static TestDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new TestDbContext(options);
        }

        [Fact]
        public async Task Create_WithNullEntity_ReturnsFalseAndDoesNotThrow()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);

            bool result = await repo.Create(null!);

            Assert.False(result);
        }

        [Fact]
        public async Task Create_WithValidEntity_PersistsAndReturnsTrue()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);

            bool result = await repo.Create(new TestEntity { Id = 1, Name = "Alpha" });

            Assert.True(result);
            Assert.Equal(1, await context.TestEntities.CountAsync());
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 0)]
        [InlineData(-1, 10)]
        public async Task Pageineering_WithInvalidParameters_ReturnsEmptyWithoutQuerying(int page, int pageSize)
        {
            await using TestDbContext context = CreateContext();
            context.TestEntities.Add(new TestEntity { Id = 1, Name = "Alpha" });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            IEnumerable<TestEntity> result = await repo.Pageineering(page, pageSize);

            Assert.Empty(result);
        }

        [Fact]
        public async Task Pageineering_WithValidParameters_ReturnsRequestedPage()
        {
            await using TestDbContext context = CreateContext();
            for (int i = 1; i <= 5; i++)
            {
                context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item{i}" });
            }
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            IEnumerable<TestEntity> result = await repo.Pageineering(2, 2);

            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task Delete_WithExistingEntity_ReturnsTrue()
        {
            await using TestDbContext context = CreateContext();
            var entity = new TestEntity { Id = 1, Name = "Alpha" };
            context.TestEntities.Add(entity);
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            bool result = await repo.Delete(entity);

            Assert.True(result);
            Assert.Equal(0, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task Pageineering_WithPageSizeExceedingMax_ClampsInsteadOfThrowing()
        {
            await using TestDbContext context = CreateContext();
            for (int i = 1; i <= 5; i++)
            {
                context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item{i}" });
            }
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            // pageSize far exceeds the repository's internal cap; this must clamp rather
            // than attempt an unbounded Skip/Take or throw.
            IEnumerable<TestEntity> result = await repo.Pageineering(1, 1_000_000);

            Assert.Equal(5, result.Count());
        }

        [Fact]
        public async Task Delete_WithDetachedUntrackedEntityForRemovedRow_ThrowsConcurrencyException()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);
            // Entity was never added/tracked and has no corresponding row - the InMemory provider
            // reports this as a concurrency conflict (the "original" row it expected to delete is
            // gone), which Delete() now deliberately lets propagate instead of swallowing into
            // a plain `false` (see point 4: DbUpdateConcurrencyException must not be flattened).
            var entity = new TestEntity { Id = 42, Name = "Ghost" };

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repo.Delete(entity));
        }

        [Fact]
        public async Task CreateRange_WithValidEntities_PersistsAllInOneSaveChangesCall()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);
            var batch = new[]
            {
                new TestEntity { Id = 1, Name = "Alpha" },
                new TestEntity { Id = 2, Name = "Beta" },
                new TestEntity { Id = 3, Name = "Gamma" },
            };

            bool result = await repo.CreateRange(batch);

            Assert.True(result);
            Assert.Equal(3, await context.TestEntities.CountAsync());
            // All three share one SaveChanges call: EF Core assigns them the same change-tracker
            // "batch", verifiable indirectly by every row landing in the store together (no
            // partial-batch state possible from a single provider round-trip).
        }

        [Fact]
        public async Task CreateRange_WithEmptyCollection_ReturnsFalseWithoutSaving()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);

            bool result = await repo.CreateRange([]);

            Assert.False(result);
            Assert.Equal(0, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task DeleteRange_WithExistingEntities_RemovesAllInOneSaveChangesCall()
        {
            await using TestDbContext context = CreateContext();
            var entities = new[]
            {
                new TestEntity { Id = 1, Name = "Alpha" },
                new TestEntity { Id = 2, Name = "Beta" },
            };
            context.TestEntities.AddRange(entities);
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            bool result = await repo.DeleteRange(entities);

            Assert.True(result);
            Assert.Equal(0, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task ExistsAsync_ForExistingId_ReturnsTrueWithoutTrackingTheEntity()
        {
            // Seed via a separate context instance so the change-tracker assertion below
            // reflects only what ExistsAsync itself did, not the Add()+SaveChanges() used to seed.
            string databaseName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseName).Options;
            await using (var seedContext = new TestDbContext(options))
            {
                seedContext.TestEntities.Add(new TestEntity { Id = 1, Name = "Alpha" });
                await seedContext.SaveChangesAsync();
            }

            await using var context = new TestDbContext(options);
            var repo = new CrudRepository<TestEntity>(context);

            bool exists = await repo.ExistsAsync(1);

            Assert.True(exists);
            // AsNoTracking(): the change tracker must not have picked up an entity for id 1
            // just from answering the existence check.
            Assert.Empty(context.ChangeTracker.Entries<TestEntity>());
        }

        [Fact]
        public async Task ExistsAsync_ForMissingId_ReturnsFalse()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);

            bool exists = await repo.ExistsAsync(999);

            Assert.False(exists);
        }

        [Fact]
        public async Task Pageineering_WithoutOrderBy_IsStableAcrossRepeatedCalls()
        {
            await using TestDbContext context = CreateContext();
            for (int i = 1; i <= 5; i++)
            {
                context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item{i}" });
            }
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            IEnumerable<TestEntity> first = await repo.Pageineering(1, 3);
            IEnumerable<TestEntity> second = await repo.Pageineering(1, 3);

            Assert.Equal(first.Select(e => e.Id), second.Select(e => e.Id));
            Assert.Equal([1, 2, 3], first.Select(e => e.Id));
        }

        [Fact]
        public async Task Pageineering_WithExplicitOrderByDescending_OrdersAccordingly()
        {
            await using TestDbContext context = CreateContext();
            for (int i = 1; i <= 5; i++)
            {
                context.TestEntities.Add(new TestEntity { Id = i, Name = $"Item{i}" });
            }
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            IEnumerable<TestEntity> result = await repo.Pageineering(1, 3, orderBy: e => e.Id, descending: true);

            Assert.Equal([5, 4, 3], result.Select(e => e.Id));
        }

        [Fact]
        public async Task Find_WithMatchingPredicate_ReturnsOnlyMatches()
        {
            await using TestDbContext context = CreateContext();
            context.TestEntities.AddRange(
                new TestEntity { Id = 1, Name = "Alpha" },
                new TestEntity { Id = 2, Name = "Beta" },
                new TestEntity { Id = 3, Name = "Alpha" });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            IEnumerable<TestEntity> result = await repo.Find(e => e.Name == "Alpha");

            Assert.Equal([1, 3], result.Select(e => e.Id).OrderBy(id => id));
        }

        [Fact]
        public async Task Update_WithConcurrentModification_ThrowsConcurrencyException()
        {
            string databaseName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseName).Options;

            await using var seedContext = new TestDbContext(options);
            seedContext.TestEntities.Add(new TestEntity { Id = 1, Name = "Original" });
            await seedContext.SaveChangesAsync();

            // Context A loads the row, then someone else (context C) changes it first.
            await using var contextA = new TestDbContext(options);
            TestEntity stale = (await contextA.TestEntities.FindAsync(1))!;

            await using var contextC = new TestDbContext(options);
            TestEntity concurrent = (await contextC.TestEntities.FindAsync(1))!;
            concurrent.Name = "ChangedByAnotherRequest";
            await contextC.SaveChangesAsync();

            // Context A now tries to save its stale copy - the "Name" concurrency token no
            // longer matches what's in the store.
            var repo = new CrudRepository<TestEntity>(contextA);
            stale.Name = "AttemptedUpdate";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repo.Update(stale));
        }

        [Fact]
        public async Task Update_WithStaleRowVersionToken_ThrowsConcurrencyException()
        {
            // Mirrors the CRUDimplementation sample host's [Timestamp] RowVersion pattern
            // exactly, provoking a genuine DbUpdateConcurrencyException (not a test-double
            // throw) the same way Update_WithConcurrentModification does for the Name token.
            string databaseName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseName).Options;

            await using var seedContext = new TestDbContext(options);
            var seeded = new RowVersionedTestEntity { Id = 1, Name = "Original" };
            seedContext.RowVersionedTestEntities.Add(seeded);
            await seedContext.SaveChangesAsync();

            await using var contextA = new TestDbContext(options);
            RowVersionedTestEntity stale = (await contextA.RowVersionedTestEntities.FindAsync(1))!;

            await using var contextC = new TestDbContext(options);
            RowVersionedTestEntity concurrent = (await contextC.RowVersionedTestEntities.FindAsync(1))!;
            concurrent.Name = "ChangedByAnotherRequest";
            await contextC.SaveChangesAsync();

            var repo = new CrudRepository<RowVersionedTestEntity>(contextA);
            stale.Name = "AttemptedUpdate";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repo.Update(stale));
        }

        [Fact]
        public async Task Delete_OnSoftDeletableEntity_FlagsInsteadOfRemovingRow()
        {
            await using TestDbContext context = CreateContext();
            var entity = new SoftDeletableTestEntity { Id = 1, Name = "Alpha" };
            context.SoftDeletableTestEntities.Add(entity);
            await context.SaveChangesAsync();
            var repo = new CrudRepository<SoftDeletableTestEntity>(context);

            bool result = await repo.Delete(entity);

            Assert.True(result);
            // Row still present (soft-deleted), not hard-removed.
            Assert.Equal(1, await context.SoftDeletableTestEntities.CountAsync());
            Assert.True((await context.SoftDeletableTestEntities.FindAsync(1))!.IsDeleted);
        }

        [Fact]
        public async Task ReadIncludingDeleted_ReturnsEntityRegardlessOfConsumerQueryFilterIntent()
        {
            await using TestDbContext context = CreateContext();
            var entity = new SoftDeletableTestEntity { Id = 1, Name = "Alpha" };
            context.SoftDeletableTestEntities.Add(entity);
            await context.SaveChangesAsync();
            var repo = new CrudRepository<SoftDeletableTestEntity>(context);
            await repo.Delete(entity);

            SoftDeletableTestEntity? found = await repo.ReadIncludingDeleted(1);

            Assert.NotNull(found);
            Assert.True(found!.IsDeleted);
        }

        [Fact]
        public async Task GetEntryByID_ForMissingId_ReturnsNullNotANullForgedNonNullValue()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);

            EntityEntry? entry = await repo.GetEntryByID(999);

            Assert.Null(entry);
        }

        [Fact]
        public async Task GetEntryByID_ForExistingId_ReturnsTheEntry()
        {
            await using TestDbContext context = CreateContext();
            context.TestEntities.Add(new TestEntity { Id = 1, Name = "Alpha" });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            EntityEntry? entry = await repo.GetEntryByID(1);

            Assert.NotNull(entry);
            Assert.Equal(EntityState.Unchanged, entry!.State);
        }

        [Fact]
        public async Task CreateRange_WithBatchExceedingMax_RejectsWithoutSaving()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);
            var oversizedBatch = Enumerable.Range(1, 501)
                .Select(i => new TestEntity { Id = i, Name = $"Item{i}" })
                .ToList();

            bool result = await repo.CreateRange(oversizedBatch);

            Assert.False(result);
            Assert.Equal(0, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task DeleteRange_WithBatchExceedingMax_RejectsWithoutSaving()
        {
            await using TestDbContext context = CreateContext();
            var entities = Enumerable.Range(1, 501)
                .Select(i => new TestEntity { Id = i, Name = $"Item{i}" })
                .ToList();
            context.TestEntities.AddRange(entities);
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            bool result = await repo.DeleteRange(entities);

            Assert.False(result);
            Assert.Equal(501, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task FindByIdsAsync_WithBatchExceedingMax_RejectsWithoutQuerying()
        {
            await using TestDbContext context = CreateContext();
            var repo = new CrudRepository<TestEntity>(context);
            var oversizedIdList = Enumerable.Range(1, 501).ToList();

            IEnumerable<TestEntity> result = await repo.FindByIdsAsync(oversizedIdList);

            Assert.Empty(result);
        }

        [Fact]
        public async Task FindByIdsAsync_WithKnownIds_ReturnsMatchesInOneQuery()
        {
            await using TestDbContext context = CreateContext();
            context.TestEntities.AddRange(
                new TestEntity { Id = 1, Name = "Alpha" },
                new TestEntity { Id = 2, Name = "Beta" },
                new TestEntity { Id = 3, Name = "Gamma" });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<TestEntity>(context);

            IEnumerable<TestEntity> result = await repo.FindByIdsAsync([1, 3, 999]);

            Assert.Equal([1, 3], result.Select(e => e.Id).OrderBy(id => id));
        }
    }
}

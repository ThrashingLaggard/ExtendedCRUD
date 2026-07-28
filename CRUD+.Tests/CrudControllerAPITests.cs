using CRUD.Controllers;
using CRUD.Models;
using CRUD.Repos;
using CRUD.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRUD_.Tests
{
    public class CrudControllerAPITests
    {
        private static (CrudControllerAPI<TestEntity> Controller, TestDbContext Context) CreateController(string? databaseName = null)
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .Options;
            var context = new TestDbContext(options);
            var repo = new CrudRepository<TestEntity>(context);
            var service = new CrudService<TestEntity>(repo);
            return (new CrudControllerAPI<TestEntity>(service), context);
        }

        [Fact]
        public async Task Post_WithInvalidModelState_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;
            controller.ModelState.AddModelError("Name", "Name is required");

            ActionResult result = await controller.Post(new TestEntity { Id = 1, Name = "Alpha" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Put_WithInvalidModelState_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;
            controller.ModelState.AddModelError("Name", "Name is required");

            ActionResult result = await controller.Put(1, new TestEntity { Id = 1, Name = "Alpha" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Post_WithValidModelState_ReturnsOk()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult result = await controller.Post(new TestEntity { Id = 1, Name = "Alpha" });

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Post_WithNullBody_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult result = await controller.Post(null!);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Put_WithNullBody_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult result = await controller.Put(1, null!);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Put_WithMismatchedRouteAndBodyId_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult result = await controller.Put(1, new TestEntity { Id = 2, Name = "Alpha" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Put_WithMatchingRouteAndBodyId_ReturnsOk()
        {
            // Seed via a separate context instance (same in-memory database) so the entity
            // pushed through Put() below isn't already being tracked by a different instance.
            string databaseName = Guid.NewGuid().ToString();
            await using (var seedContext = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName).Options))
            {
                seedContext.TestEntities.Add(new TestEntity { Id = 1, Name = "Original" });
                await seedContext.SaveChangesAsync();
            }

            var (controller, context) = CreateController(databaseName);
            await using var _ = context;

            // Name doubles as the concurrency token in TestDbContext (see Update_WithConcurrentModification
            // below) - keep it unchanged here so this id-matching test isn't itself a concurrency conflict.
            ActionResult result = await controller.Put(1, new TestEntity { Id = 1, Name = "Original" });

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task PostRange_WithEntities_ReturnsOkAndPersistsAll()
        {
            var (controller, context) = CreateController();
            await using var _ = context;
            var batch = new[]
            {
                new TestEntity { Id = 1, Name = "Alpha" },
                new TestEntity { Id = 2, Name = "Beta" },
            };

            ActionResult result = await controller.PostRange(batch);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(2, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task PostRange_WithEmptyBody_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult result = await controller.PostRange([]);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task DeleteRange_WithExistingIds_ReturnsOkAndRemovesAll()
        {
            string databaseName = Guid.NewGuid().ToString();
            await using (var seedContext = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName).Options))
            {
                seedContext.TestEntities.AddRange(
                    new TestEntity { Id = 1, Name = "Alpha" },
                    new TestEntity { Id = 2, Name = "Beta" });
                await seedContext.SaveChangesAsync();
            }

            var (controller, context) = CreateController(databaseName);
            await using var _ = context;

            ActionResult result = await controller.DeleteRange([1, 2]);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(0, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task DeleteRange_WithNoMatchingIds_ReturnsNotFound()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult result = await controller.DeleteRange([999, 1000]);

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(404, status.StatusCode);
        }

        [Fact]
        public async Task Find_WithKnownProperty_ReturnsOkWithMatches()
        {
            var (controller, context) = CreateController();
            await using var _ = context;
            context.TestEntities.AddRange(
                new TestEntity { Id = 1, Name = "Alpha" },
                new TestEntity { Id = 2, Name = "Beta" });
            await context.SaveChangesAsync();

            ActionResult<IEnumerable<TestEntity>> result = await controller.Find(new CrudFilterRequest { PropertyName = "Name", Value = "Alpha" });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var matches = Assert.IsAssignableFrom<IEnumerable<TestEntity>>(ok.Value);
            Assert.Equal([1], matches.Select(e => e.Id));
        }

        [Fact]
        public async Task Find_WithUnknownProperty_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;

            ActionResult<IEnumerable<TestEntity>> result = await controller.Find(new CrudFilterRequest { PropertyName = "DoesNotExist", Value = "x" });

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task Put_WithConcurrentModification_ReturnsConflict()
        {
            string databaseName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseName).Options;

            await using var seedContext = new TestDbContext(options);
            seedContext.TestEntities.Add(new TestEntity { Id = 1, Name = "Original" });
            await seedContext.SaveChangesAsync();

            // Someone else changes the row first.
            await using (var concurrentContext = new TestDbContext(options))
            {
                TestEntity row = (await concurrentContext.TestEntities.FindAsync(1))!;
                row.Name = "ChangedByAnotherRequest";
                await concurrentContext.SaveChangesAsync();
            }

            var (controller, context) = CreateController(databaseName);
            await using var _ = context;
            // Body still carries the stale "Original" concurrency token.
            var stale = new TestEntity { Id = 1, Name = "Original" };

            ActionResult result = await controller.Put(1, stale);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.NotNull(conflict.Value);
        }

        [Fact]
        public async Task Find_WithNotFilterableProperty_ReturnsSameShapeAsUnknownProperty()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var context = new TestDbContext(options);
            context.FilterableTestEntities.Add(new FilterableTestEntity { Id = 1, Name = "Alpha", Secret = "shh" });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<FilterableTestEntity>(context);
            var service = new CrudService<FilterableTestEntity>(repo);
            var controller = new CrudControllerAPI<FilterableTestEntity>(service);

            ActionResult<IEnumerable<FilterableTestEntity>> excludedResult =
                await controller.Find(new CrudFilterRequest { PropertyName = "Secret", Value = "shh" });
            ActionResult<IEnumerable<FilterableTestEntity>> reallyUnknownResult =
                await controller.Find(new CrudFilterRequest { PropertyName = "Secret2", Value = "shh" });

            var excludedBadRequest = Assert.IsType<BadRequestObjectResult>(excludedResult.Result);
            var reallyUnknownBadRequest = Assert.IsType<BadRequestObjectResult>(reallyUnknownResult.Result);
            // Both must be the exact "Unknown property '<name>'" template - i.e. a [CrudNotFilterable]
            // property is rejected with the identical wording a genuinely nonexistent property gets,
            // not a distinct "excluded" message that would reveal the property exists.
            Assert.Equal($"Unknown property 'Secret' on {nameof(FilterableTestEntity)}.", excludedBadRequest.Value);
            Assert.Equal($"Unknown property 'Secret2' on {nameof(FilterableTestEntity)}.", reallyUnknownBadRequest.Value);
        }

        [Fact]
        public async Task Find_WithGuidProperty_ParsesAndMatches()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var context = new TestDbContext(options);
            var targetRef = Guid.NewGuid();
            context.FilterableTestEntities.AddRange(
                new FilterableTestEntity { Id = 1, Name = "Alpha", ExternalRef = targetRef },
                new FilterableTestEntity { Id = 2, Name = "Beta", ExternalRef = Guid.NewGuid() });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<FilterableTestEntity>(context);
            var service = new CrudService<FilterableTestEntity>(repo);
            var controller = new CrudControllerAPI<FilterableTestEntity>(service);

            ActionResult<IEnumerable<FilterableTestEntity>> result = await controller.Find(
                new CrudFilterRequest { PropertyName = "ExternalRef", Value = targetRef.ToString() });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var matches = Assert.IsAssignableFrom<IEnumerable<FilterableTestEntity>>(ok.Value);
            Assert.Equal([1], matches.Select(e => e.Id));
        }

        [Fact]
        public async Task Find_WithEnumProperty_ParsesAndMatches()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var context = new TestDbContext(options);
            context.FilterableTestEntities.AddRange(
                new FilterableTestEntity { Id = 1, Name = "Alpha", Status = TestStatus.Archived },
                new FilterableTestEntity { Id = 2, Name = "Beta", Status = TestStatus.Active });
            await context.SaveChangesAsync();
            var repo = new CrudRepository<FilterableTestEntity>(context);
            var service = new CrudService<FilterableTestEntity>(repo);
            var controller = new CrudControllerAPI<FilterableTestEntity>(service);

            ActionResult<IEnumerable<FilterableTestEntity>> result = await controller.Find(
                new CrudFilterRequest { PropertyName = "Status", Value = "archived" });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var matches = Assert.IsAssignableFrom<IEnumerable<FilterableTestEntity>>(ok.Value);
            Assert.Equal([1], matches.Select(e => e.Id));
        }

        [Fact]
        public async Task DeleteRange_ResolvesIdsViaSingleQueryNotAPerIdReadLoop()
        {
            string databaseName = Guid.NewGuid().ToString();
            await using (var seedContext = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName).Options))
            {
                seedContext.TestEntities.AddRange(
                    new TestEntity { Id = 1, Name = "Alpha" },
                    new TestEntity { Id = 2, Name = "Beta" },
                    new TestEntity { Id = 3, Name = "Gamma" });
                await seedContext.SaveChangesAsync();
            }

            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            await using var context = new SpyingTestDbContext(options);
            var repo = new CrudRepository<TestEntity>(context);
            var service = new CrudService<TestEntity>(repo);
            var controller = new CrudControllerAPI<TestEntity>(service);

            ActionResult result = await controller.DeleteRange([1, 2, 3]);

            Assert.IsType<OkObjectResult>(result);
            // DeleteRange must resolve all three ids via FindByIdsAsync's single Where(...)
            // query, never through CrudRepository<T>.Read's per-id FindAsync - the whole point
            // of item 3's fix. FindAsync is what the old per-id loop used.
            Assert.Equal(0, context.FindAsyncCallCount);
        }

        [Fact]
        public async Task PostRange_WithBatchExceedingMax_ReturnsBadRequestWithoutSaving()
        {
            var (controller, context) = CreateController();
            await using var _ = context;
            var oversizedBatch = Enumerable.Range(1, 501)
                .Select(i => new TestEntity { Id = i, Name = $"Item{i}" })
                .ToList();

            ActionResult result = await controller.PostRange(oversizedBatch);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, await context.TestEntities.CountAsync());
        }

        [Fact]
        public async Task DeleteRange_WithIdBatchExceedingMax_ReturnsBadRequest()
        {
            var (controller, context) = CreateController();
            await using var _ = context;
            var oversizedIdList = Enumerable.Range(1, 501).ToList();

            ActionResult result = await controller.DeleteRange(oversizedIdList);

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}

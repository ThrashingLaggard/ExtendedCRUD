using Microsoft.EntityFrameworkCore;

namespace CRUD_.Tests
{
    /// <summary>
    /// Counts calls to <see cref="DbContext.FindAsync{TEntity}(object?[], CancellationToken)"/>
    /// (what <c>CrudRepository&lt;T&gt;.Read</c> uses) so tests can assert "no per-id Read loop"
    /// directly, without depending on EF Core's query-plan cache — which is shared/static across
    /// DbContext instances within a test run and makes a naive "count compiled queries" approach
    /// (via <c>IQueryExpressionInterceptor</c>) order-dependent and unreliable: an identical query
    /// shape compiled by an earlier test is silently reused, so the interceptor doesn't fire again.
    /// </summary>
    public class SpyingTestDbContext(DbContextOptions<TestDbContext> options) : TestDbContext(options)
    {
        public int FindAsyncCallCount { get; private set; }

        public override ValueTask<TEntity?> FindAsync<TEntity>(object?[]? keyValues, CancellationToken cancellationToken) where TEntity : class
        {
            FindAsyncCallCount++;
            return base.FindAsync<TEntity>(keyValues, cancellationToken);
        }
    }
}

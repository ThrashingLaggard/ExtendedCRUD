using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Mutation operations that stage a change on the change tracker and return without saving,
    /// leaving <see cref="SaveChangesAsync"/> — and any surrounding transaction — entirely to
    /// the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other mutating member of this library saves immediately, which is the right default
    /// for one-entity-per-request work but makes multi-phase writes impossible to express. A
    /// batch that has to flush at specific points inside a single transaction — because the
    /// intermediate states would otherwise violate a unique index, and only the caller knows
    /// where those boundaries are — needs to control the flush itself:
    /// </para>
    /// <code>
    /// await using var db = await factory.CreateDbContextAsync(ct);
    /// await using var tx = await db.Database.BeginTransactionAsync(ct);
    /// var crud = new CrudRepository&lt;SectionEntity&gt;(db);
    ///
    /// await crud.UpdateRangeNoSaveAsync(closedSections, ct);
    /// await crud.SaveChangesAsync(ct);   // phase 1 boundary
    ///
    /// await crud.UpdateRangeNoSaveAsync(vacatedSections, ct);
    /// await crud.SaveChangesAsync(ct);   // phase 2 boundary
    ///
    /// await crud.AddRangeNoSaveAsync(newSections, ct);
    /// await crud.UpdateRangeNoSaveAsync(restoredSections, ct);
    /// await crud.SaveChangesAsync(ct);   // phase 3 boundary
    ///
    /// await tx.CommitAsync(ct);
    /// </code>
    /// <para>
    /// The guarantees that make the above safe hold for the whole library, not just these
    /// members: the <c>DbContext</c> is supplied by the caller and is never created, cached,
    /// owned or disposed here, and no operation ever opens, commits or rolls back a transaction
    /// of its own. Work simply enlists in whatever transaction the supplied context already has.
    /// Repositories are cheap, so resolving a context per unit of work — from an
    /// <c>IDbContextFactory&lt;TContext&gt;</c> for background work, or from a scoped
    /// registration in a request — and constructing a repository around it is the intended
    /// usage. Do not register a repository as a singleton: it would capture one context.
    /// </para>
    /// <para>
    /// Unlike the saving bulk members, the range operations here impose no batch-size cap. The
    /// cap exists to bound a single database round-trip, and staging performs none — the cost of
    /// an oversized batch lands on the caller's own <see cref="SaveChangesAsync"/>, where the
    /// caller can see and size it.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface ICrudStaging<T> where T : class
    {
        /// <summary>Stages an insert. Does not save.</summary>
        /// <param name="entity">The entity to insert.</param>
        /// <param name="cancellationToken">Token used to cancel any value generation the insert requires.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
        Task AddNoSaveAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Stages several inserts. Does not save.</summary>
        /// <param name="entities">The entities to insert.</param>
        /// <param name="cancellationToken">Token used to cancel any value generation the inserts require.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is <see langword="null"/>.</exception>
        Task AddRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>Stages an update. Does not save.</summary>
        /// <param name="entity">The entity to update.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
        Task UpdateNoSaveAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Stages several updates. Does not save.</summary>
        /// <param name="entities">The entities to update.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is <see langword="null"/>.</exception>
        Task UpdateRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>Stages a row removal. Does not save, and never soft-deletes.</summary>
        /// <param name="entity">The entity whose row should be removed.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
        Task RemoveNoSaveAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Stages several row removals. Does not save, and never soft-deletes.</summary>
        /// <param name="entities">The entities whose rows should be removed.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is <see langword="null"/>.</exception>
        Task RemoveRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// Flushes everything currently staged on the caller's context.
        /// </summary>
        /// <remarks>
        /// Saves the whole change tracker, not only changes staged through this repository —
        /// it is the caller's context, and a repository-scoped flush is not something EF Core
        /// offers. Never commits: a transaction opened by the caller stays open.
        /// </remarks>
        /// <param name="cancellationToken">Token used to cancel the save.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException">A concurrent change was detected.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}

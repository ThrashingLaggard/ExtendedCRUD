using CRUD.Exceptions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// The <c>Async</c>-suffixed core operations, which report failure by throwing rather than
    /// by returning <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These sit alongside the original <see cref="IBaseCrud{T}"/> members rather than replacing
    /// them, because the two have genuinely different contracts and both are legitimate:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="IBaseCrud{T}.Create"/> and friends log the failure and
    ///   return <see langword="false"/>, so a caller that does not wrap every call in a
    ///   <c>try</c> still gets a defined outcome.</description></item>
    ///   <item><description>The members here let the failure propagate, so a caller that maps
    ///   data-access failures onto its own error types (and must not silently treat a failed
    ///   write as a no-op) can do so. Nothing is swallowed.</description></item>
    /// </list>
    /// <para>
    /// New code should prefer these. The originals are kept working, unchanged, for existing
    /// consumers.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface ICrudAsync<T> where T : class
    {
        /// <summary>Creates an entity and saves immediately.</summary>
        /// <param name="entity">The entity to create.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> CreateAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Creates several entities and saves them in one round-trip.</summary>
        /// <param name="entities">The entities to create.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is <see langword="null"/>.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> CreateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>Updates an entity and saves immediately.</summary>
        /// <param name="entity">The entity to update.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException">A concurrent change was detected.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Updates several entities and saves them in one round-trip.</summary>
        /// <param name="entities">The entities to update.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is <see langword="null"/>.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the row for <paramref name="entity"/> and saves immediately, regardless of
        /// whether the entity is soft-deletable.
        /// </summary>
        /// <remarks>
        /// Named explicitly rather than being another <c>Delete</c>: unlike
        /// <see cref="IBaseCrud{T}.Delete"/>, this never reinterprets the request as a soft
        /// delete, so a caller asking to remove a row cannot silently get a flag update instead.
        /// Use <see cref="ICrudSoftDelete{T}.SoftDeleteAsync"/> for the closing behavior.
        /// </remarks>
        /// <param name="entity">The entity whose row should be removed.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException">A concurrent change was detected.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> HardDeleteAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Removes several rows and saves in one round-trip, never soft-deleting.</summary>
        /// <param name="entities">The entities whose rows should be removed.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is <see langword="null"/>.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">The write failed.</exception>
        Task<int> HardDeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
    }
}

using System.Runtime.CompilerServices;
using System.Threading;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Bulk (multi-entity, single round-trip) CRUD extensions for <typeparamref name="T"/>.
    /// Kept as its own interface, mirroring the existing split of concerns
    /// (<see cref="IBaseCrud{T}"/> / <see cref="IBaseCrudExtensions{T}"/> /
    /// <see cref="IEfCoreCrudExtensions{T}"/>) rather than being bolted onto <see cref="IBaseCrud{T}"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IBulkCrudExtensions<T> where T : class
    {
        /// <summary>
        /// Creates multiple entities in a single database round-trip.
        /// </summary>
        /// <param name="entities">The entities to create. A null or empty sequence returns <see langword="false"/> without touching the database.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<bool> CreateRange(IEnumerable<T> entities, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes multiple entities in a single database round-trip.
        /// </summary>
        /// <param name="entities">The entities to delete. A null or empty sequence returns <see langword="false"/> without touching the database.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<bool> DeleteRange(IEnumerable<T> entities, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Looks up multiple entities by id in a single query, so bulk operations that need to
        /// resolve ids to entities don't have to loop a per-id <c>Read</c>.
        /// </summary>
        /// <param name="ids">The ids to look up. A null or empty sequence returns an empty result without querying.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the query.</param>
        Task<IEnumerable<T>> FindByIdsAsync(IEnumerable<int> ids, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);
    }
}

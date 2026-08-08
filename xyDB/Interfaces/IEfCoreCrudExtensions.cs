using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Providing EF Core extensions for IBaseCrud
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IEfCoreCrudExtensions<T> where T : class
    {
        /// <summary>
        /// Get the EF Core change-tracking entry for the given id, or <see langword="null"/> if
        /// no entity exists for that id.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<EntityEntry?> GetEntryByID(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether an entity with the given id exists, without materializing it.
        /// </summary>
        /// <remarks>
        /// Assumes <typeparamref name="T"/> exposes a conventional <c>int</c> "Id" property,
        /// the same convention already relied on by <see cref="IBaseCrud{T}.Read"/> and by
        /// the optional CRUD+.AspNetCore controller's id-matching helper.
        /// </remarks>
        /// <param name="id">The id to check for.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<bool> ExistsAsync(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads an entity by id, deliberately bypassing any global EF Core query filter the
        /// consuming application may have configured on its <c>DbContext</c> (e.g. a soft-delete
        /// filter via <see cref="ISoftDeletable"/>/<c>HasQueryFilter</c>).
        /// </summary>
        /// <remarks>
        /// This is the explicit "show it anyway" path for soft-deleted (or otherwise
        /// filtered-out) rows — e.g. an admin "show deleted" view or a recovery flow.
        /// <see cref="IBaseCrud{T}.Read"/> stays filter-respecting and unchanged; use this
        /// method only where bypassing the consumer's filter is actually intended, since it
        /// ignores <em>all</em> configured query filters, not just a soft-delete one.
        /// </remarks>
        /// <param name="id">The id of the entity to fetch.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<T?> ReadIncludingDeleted(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);
    }
}

using CRUD.Exceptions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Key-based reads that work for any primary key the EF Core model defines, not only an
    /// <c>int</c> property named <c>Id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is discovered from the model
    /// (<c>DbContext.Model.FindEntityType(typeof(T)).FindPrimaryKey()</c>), which has two
    /// consequences worth stating plainly:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>A key named <c>NodeId</c>, <c>SectionId</c>, <c>Slug</c> — anything —
    ///   works, with no interface to implement and no naming convention to follow.</description></item>
    ///   <item><description>A property called <c>Id</c> that is <em>not</em> the key is never
    ///   treated as one. An entity whose surrogate key is <c>NodeId</c> and which separately
    ///   carries a non-unique business identifier called <c>Id</c> is read by <c>NodeId</c>,
    ///   as configured.</description></item>
    /// </list>
    /// <para>
    /// Entity types that cannot be addressed by one scalar key — keyless ones, composite-key
    /// ones, ones absent from the model — do not prevent a repository from being constructed or
    /// from using its other members. Only calling something here raises
    /// <see cref="CrudKeyException"/>, and it says which of those three cases applies.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface ICrudKeyAccess<T> where T : class
    {
        /// <summary>
        /// Reads the entity with the given primary-key value, honoring global query filters.
        /// </summary>
        /// <remarks>
        /// Filter-respecting on purpose, and it queries the database rather than consulting the
        /// change tracker first. That distinction matters for soft delete: a lookup that
        /// returned a closed row — because it happened to be tracked, or because filters were
        /// skipped — would let a caller revive history that is supposed to stay closed. A row
        /// filtered out is reported as absent, exactly like one that never existed. Use
        /// <see cref="ReadByKeyIncludingFilteredAsync"/> for the deliberate opposite.
        /// </remarks>
        /// <param name="keyValue">The primary-key value. Converted to the key's CLR type if needed.</param>
        /// <param name="tracked"><see langword="true"/> to return a tracked entity ready to update. Defaults to <see langword="false"/>.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The entity, or <see langword="null"/> if no live row has that key.</returns>
        /// <exception cref="CrudKeyException">The entity type cannot be addressed by a single key value.</exception>
        Task<T?> ReadByKeyAsync(object keyValue, bool tracked = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the entity with the given primary-key value, bypassing every global query
        /// filter.
        /// </summary>
        /// <param name="keyValue">The primary-key value.</param>
        /// <param name="tracked"><see langword="true"/> to return a tracked entity. Defaults to <see langword="false"/>.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The entity, or <see langword="null"/> if no row at all has that key.</returns>
        /// <exception cref="CrudKeyException">The entity type cannot be addressed by a single key value.</exception>
        Task<T?> ReadByKeyIncludingFilteredAsync(object keyValue, bool tracked = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a row with the given primary-key value exists, without materializing it.
        /// </summary>
        /// <param name="keyValue">The primary-key value.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="CrudKeyException">The entity type cannot be addressed by a single key value.</exception>
        Task<bool> ExistsByKeyAsync(object keyValue, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads every entity whose primary key is in <paramref name="keyValues"/>, in one query.
        /// </summary>
        /// <param name="keyValues">The primary-key values to look up.</param>
        /// <param name="tracked"><see langword="true"/> to return tracked entities. Defaults to <see langword="false"/>.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The matching entities; keys with no live row are simply absent.</returns>
        /// <exception cref="CrudKeyException">The entity type cannot be addressed by a single key value.</exception>
        Task<IReadOnlyList<T>> ReadByKeysAsync(IEnumerable<object> keyValues, bool tracked = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the primary-key value off <paramref name="entity"/> without touching the change
        /// tracker.
        /// </summary>
        /// <remarks>
        /// Reads the CLR property directly rather than going through <c>DbContext.Entry</c>,
        /// because <c>Entry</c> would start tracking an entity the caller only wanted to
        /// inspect — which matters when the entity came off a request body and is not meant to
        /// be attached yet.
        /// </remarks>
        /// <param name="entity">The entity to inspect.</param>
        /// <param name="keyValue">Receives the key value, or <see langword="null"/> when this returns <see langword="false"/>.</param>
        /// <returns>
        /// <see langword="false"/> — rather than throwing — when the key cannot be read at all:
        /// a keyless, composite-key or unmapped type, or a shadow key that exists only in the
        /// model and has no CLR property to read. This is a question a caller asks in order to
        /// decide something, so "no" is an answer, not a failure.
        /// </returns>
        bool TryGetKeyValue(T entity, out object? keyValue);
    }
}

using CRUD.Exceptions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Soft delete against a caller-configured marker property, as a first-class operation
    /// distinct from removing a row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Soft and hard delete are both legitimate and often coexist in one schema: history-bearing
    /// tables get closed, tables fully derived from a current source get deleted and rewritten.
    /// This library therefore never decides between them on the caller's behalf — the member
    /// name says which one happens.
    /// </para>
    /// <para>
    /// The members here require the repository to have been constructed with a
    /// <see cref="Options.CrudSoftDeleteOptions{T}"/>. The marker can be a nullable timestamp
    /// (<c>ValidTo</c>), a boolean, or anything else; the entity does not implement any
    /// interface and the property is not required to have a particular name.
    /// </para>
    /// <para>
    /// The write is a plain <c>UPDATE</c> that marks <em>only</em> the marker property as
    /// modified. No other column is touched, which keeps a concurrent change to an unrelated
    /// field intact and makes the statement match what a schema's
    /// <c>AFTER UPDATE OF &lt;marker&gt;</c> trigger expects.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface ICrudSoftDelete<T> where T : class
    {
        /// <summary>Whether this repository was configured with a soft-delete marker.</summary>
        /// <remarks>
        /// Lets a caller branch on the configuration instead of catching an exception to find
        /// out.
        /// </remarks>
        bool IsSoftDeleteConfigured { get; }

        /// <summary>Closes the row by stamping the marker property, and saves.</summary>
        /// <param name="entity">The entity to close.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="CrudConfigurationException">No soft-delete marker was configured.</exception>
        /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException">A concurrent change was detected.</exception>
        Task<int> SoftDeleteAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Stages the marker stamp without saving.</summary>
        /// <param name="entity">The entity to close.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="CrudConfigurationException">No soft-delete marker was configured.</exception>
        Task SoftDeleteNoSaveAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>Stages the marker stamp for several entities without saving.</summary>
        /// <param name="entities">The entities to close.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <exception cref="CrudConfigurationException">No soft-delete marker was configured.</exception>
        Task SoftDeleteRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>Reopens a closed row by resetting the marker property, and saves.</summary>
        /// <param name="entity">The entity to reopen.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of state entries written to the database.</returns>
        /// <exception cref="CrudConfigurationException">No soft-delete marker was configured.</exception>
        Task<int> RestoreAsync(T entity, CancellationToken cancellationToken = default);
    }
}

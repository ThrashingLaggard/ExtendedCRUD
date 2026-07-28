using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Standard asynchronous CRUD operations for an entity of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface IBaseCrud<T> where T : class
    {
        /// <summary>Create a new entity.</summary>
        /// <param name="entity">The entity to create.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<bool> Create(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>Read an entity by its id.</summary>
        /// <param name="id">The id of the entity to read.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<T?> Read(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>Update an existing entity.</summary>
        /// <param name="entity">The entity to update.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<bool> Update(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>Delete an existing entity.</summary>
        /// <param name="entity">The entity to delete.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<bool> Delete(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);
    }
}

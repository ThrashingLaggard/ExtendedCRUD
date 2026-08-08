using CRUD.Interfaces;
using CRUD.Repos;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace CRUD.Services
{
    /// <summary>
    /// The logic surrounding the CrudRepository
    /// </summary>
    /// <remarks>
    /// Currently a thin pass-through to <see cref="CrudRepository{T}"/>; kept as a separate
    /// layer so consumers have a seam for cross-cutting concerns (validation, caching, events)
    /// without having to touch the repository or the controller. All pass-throughs, including
    /// the concurrency-sensitive <see cref="Update"/>/<see cref="Delete"/>/<see cref="DeleteRange"/>,
    /// let exceptions (e.g. <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>)
    /// propagate unchanged from <see cref="CrudRepository{T}"/> - this layer does not add its own
    /// try/catch around them.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public partial class CrudService<T>(CrudRepository<T> crudRepository) : IExtendedCrud<T> where T : class
    {

        /// <summary>
        /// Providing access to the target DB
        /// </summary>
        private readonly CrudRepository<T> _crudRepository = crudRepository;

        #region "CRUD"
        /// <summary>
        /// Gives a new entity to the CrudRepo
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<bool> Create(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.Create(entity, callerName, cancellationToken);
        }

        /// <summary>
        /// Calls Read(id) from CRUD-Repo
        /// </summary>
        /// <param name="id"></param>
        /// <param name="callerName"></param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<T?> Read(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.Read(id, callerName, cancellationToken);
        }

        /// <summary>
        /// Gives new data for the target entity to the CrudRepo
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<bool> Update(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.Update(entity, callerName, cancellationToken);

        }

        /// <summary>
        /// Give the target to the CrudRepo to delete it
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<bool> Delete(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.Delete(entity, callerName, cancellationToken);
        }

        #endregion

        #region "Bulk"

        /// <summary>
        /// Calls CreateRange from CrudRepo.
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<bool> CreateRange(IEnumerable<T> entities, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.CreateRange(entities, callerName, cancellationToken);
        }

        /// <summary>
        /// Calls DeleteRange from CrudRepo.
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<bool> DeleteRange(IEnumerable<T> entities, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.DeleteRange(entities, callerName, cancellationToken);
        }

        /// <summary>
        /// Calls FindByIdsAsync from CrudRepo.
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<IEnumerable<T>> FindByIdsAsync(IEnumerable<int> ids, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.FindByIdsAsync(ids, callerName, cancellationToken);
        }

        #endregion

        #region "Extensions"
        /// <summary>
        /// Calls GetAll() from the CrudRepo
        /// </summary>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<IEnumerable<T?>> GetAll([CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.GetAll(callerName, cancellationToken);
        }

        /// <summary>
        /// Calls Pageineering from CrudRepo
        /// </summary>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="callerName"></param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <param name="orderBy">Optional sort key selector; falls back to ordering by "Id" when omitted.</param>
        /// <param name="descending">When <see langword="true"/>, sorts descending instead of ascending.</param>
        /// <returns></returns>
        public Task<IEnumerable<T>> Pageineering(int page, int pageSize, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default, Expression<Func<T, object>>? orderBy = null, bool descending = false)
        {
            return _crudRepository.Pageineering(page, pageSize, callerName, cancellationToken, orderBy, descending);
        }

        /// <summary>
        /// Calls Find from CrudRepo.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.Find(predicate, callerName, cancellationToken);
        }
        #endregion

        #region "EF Core"


        /// <summary>
        /// Get a EntityEntry for the given ID, or <see langword="null"/> if no entity exists
        /// for that id.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="callerName"></param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public async Task<EntityEntry?> GetEntryByID(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {

            return await _crudRepository.GetEntryByID(id, callerName, cancellationToken);
        }

        /// <summary>
        /// Calls ExistsAsync from CrudRepo.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<bool> ExistsAsync(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.ExistsAsync(id, callerName, cancellationToken);
        }

        /// <summary>
        /// Calls ReadIncludingDeleted from CrudRepo.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public Task<T?> ReadIncludingDeleted(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            return _crudRepository.ReadIncludingDeleted(id, callerName, cancellationToken);
        }

        #endregion
    }
}

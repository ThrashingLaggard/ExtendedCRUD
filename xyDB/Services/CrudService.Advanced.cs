using CRUD.Interfaces;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Services
{
    /// <summary>
    /// Passes the 1.0.2 repository capabilities through the service layer, so a consumer that
    /// depends on <see cref="CrudService{T}"/> is not forced back down to the repository (or to
    /// a raw <c>DbContext</c>) the moment it needs staging, key access, or a queryable.
    /// </summary>
    /// <remarks>
    /// Pass-through only, exactly like the original members: no try/catch is added here, so
    /// every exception — including the defined <see cref="Exceptions.CrudException"/> types and
    /// EF Core's own <c>DbUpdateException</c>/<c>DbUpdateConcurrencyException</c> — reaches the
    /// caller unchanged. The seam stays available for consumers that want to add validation,
    /// caching or events without touching the repository.
    /// </remarks>
    public partial class CrudService<T> : IAdvancedCrud<T> where T : class
    {
        #region "Queryable access"

        /// <inheritdoc />
        public IQueryable<T> Query(bool tracked = false, bool ignoreQueryFilters = false) =>
            _crudRepository.Query(tracked, ignoreQueryFilters);

        #endregion

        #region "Key access"

        /// <inheritdoc />
        public Task<T?> ReadByKeyAsync(object keyValue, bool tracked = false, CancellationToken cancellationToken = default) =>
            _crudRepository.ReadByKeyAsync(keyValue, tracked, cancellationToken);

        /// <inheritdoc />
        public Task<T?> ReadByKeyIncludingFilteredAsync(object keyValue, bool tracked = false, CancellationToken cancellationToken = default) =>
            _crudRepository.ReadByKeyIncludingFilteredAsync(keyValue, tracked, cancellationToken);

        /// <inheritdoc />
        public Task<bool> ExistsByKeyAsync(object keyValue, CancellationToken cancellationToken = default) =>
            _crudRepository.ExistsByKeyAsync(keyValue, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<T>> ReadByKeysAsync(IEnumerable<object> keyValues, bool tracked = false, CancellationToken cancellationToken = default) =>
            _crudRepository.ReadByKeysAsync(keyValues, tracked, cancellationToken);

        /// <inheritdoc />
        public bool TryGetKeyValue(T entity, out object? keyValue) =>
            _crudRepository.TryGetKeyValue(entity, out keyValue);

        #endregion

        #region "Staging (no save)"

        /// <inheritdoc />
        public Task AddNoSaveAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.AddNoSaveAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task AddRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.AddRangeNoSaveAsync(entities, cancellationToken);

        /// <inheritdoc />
        public Task UpdateNoSaveAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.UpdateNoSaveAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task UpdateRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.UpdateRangeNoSaveAsync(entities, cancellationToken);

        /// <inheritdoc />
        public Task RemoveNoSaveAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.RemoveNoSaveAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task RemoveRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.RemoveRangeNoSaveAsync(entities, cancellationToken);

        /// <inheritdoc />
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _crudRepository.SaveChangesAsync(cancellationToken);

        #endregion

        #region "Async core operations"

        /// <inheritdoc />
        public Task<int> CreateAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.CreateAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task<int> CreateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.CreateRangeAsync(entities, cancellationToken);

        /// <inheritdoc />
        public Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.UpdateAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.UpdateRangeAsync(entities, cancellationToken);

        /// <inheritdoc />
        public Task<int> HardDeleteAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.HardDeleteAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task<int> HardDeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.HardDeleteRangeAsync(entities, cancellationToken);

        #endregion

        #region "Set-based operations"

        /// <inheritdoc />
        public Task<int> ExecuteUpdateAsync(
            Expression<Func<T, bool>> predicate,
#if NET10_0_OR_GREATER
            Action<UpdateSettersBuilder<T>> setters,
#else
            Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> setters,
#endif
            CancellationToken cancellationToken = default) =>
            _crudRepository.ExecuteUpdateAsync(predicate, setters, cancellationToken);

        /// <inheritdoc />
        public Task<int> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            _crudRepository.ExecuteDeleteAsync(predicate, cancellationToken);

        #endregion

        #region "Soft delete"

        /// <inheritdoc />
        public bool IsSoftDeleteConfigured => _crudRepository.IsSoftDeleteConfigured;

        /// <inheritdoc />
        public Task<int> SoftDeleteAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.SoftDeleteAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task SoftDeleteNoSaveAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.SoftDeleteNoSaveAsync(entity, cancellationToken);

        /// <inheritdoc />
        public Task SoftDeleteRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
            _crudRepository.SoftDeleteRangeNoSaveAsync(entities, cancellationToken);

        /// <inheritdoc />
        public Task<int> RestoreAsync(T entity, CancellationToken cancellationToken = default) =>
            _crudRepository.RestoreAsync(entity, cancellationToken);

        #endregion
    }
}

using CRUD.Exceptions;
using CRUD.Interfaces;
using CRUD.Internal;
using CRUD.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Repos
{
    /// <summary>
    /// The 1.0.2 capabilities of <see cref="CrudRepository{T}"/>: model-driven key access,
    /// no-save staging under a caller-owned transaction, set-based writes, the queryable escape
    /// hatch, and configurable soft delete.
    /// </summary>
    /// <remarks>
    /// Split into its own file purely for readability. Everything here follows one rule the
    /// original members follow too: the <c>DbContext</c> belongs to the caller. Nothing below
    /// constructs, disposes, or replaces it, and nothing opens or commits a transaction.
    /// </remarks>
    /// <typeparam name="T">The entity type this repository operates on.</typeparam>
    public partial class CrudRepository<T> : ICrudQueryAccess<T>, ICrudKeyAccess<T>, ICrudStaging<T>, ICrudAsync<T> where T : class
    {
        #region "Queryable access"

        /// <inheritdoc />
        public IQueryable<T> Query(bool tracked = false, bool ignoreQueryFilters = false)
        {
            IQueryable<T> query = _context.Set<T>();

            if (!tracked)
            {
                query = query.AsNoTracking();
            }

            if (ignoreQueryFilters)
            {
                query = query.IgnoreQueryFilters();
            }

            return query;
        }

        #endregion
        #region "Key access"

        /// <inheritdoc />
        public async Task<T?> ReadByKeyAsync(object keyValue, bool tracked = false, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyValue);

            // Deliberately a query, not DbContext.FindAsync: Find would hand back an already
            // tracked entity without applying global query filters, so a row closed earlier in
            // this same unit of work could reappear as if it were live.
            return await Query(tracked)
                .FirstOrDefaultAsync(CrudEntityKey.BuildKeyEqualsPredicate<T>(_context, keyValue), cancellationToken);
        }

        /// <inheritdoc />
        public async Task<T?> ReadByKeyIncludingFilteredAsync(object keyValue, bool tracked = false, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyValue);

            return await Query(tracked, ignoreQueryFilters: true)
                .FirstOrDefaultAsync(CrudEntityKey.BuildKeyEqualsPredicate<T>(_context, keyValue), cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsByKeyAsync(object keyValue, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyValue);

            return await Query()
                .AnyAsync(CrudEntityKey.BuildKeyEqualsPredicate<T>(_context, keyValue), cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> ReadByKeysAsync(IEnumerable<object> keyValues, bool tracked = false, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyValues);

            List<object> keys = keyValues.ToList();
            if (keys.Count == 0)
            {
                // No keys means no rows by definition; querying for that would be a pointless
                // round-trip, and an empty IN () is not universally valid SQL anyway.
                return Array.Empty<T>();
            }

            return await Query(tracked)
                .Where(CrudEntityKey.BuildKeyContainsPredicate<T>(_context, keys))
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public bool TryGetKeyValue(T entity, out object? keyValue)
        {
            keyValue = null;
            if (entity is null)
            {
                return false;
            }

            IReadOnlyList<IProperty> keyProperties = CrudEntityKey.GetKeyProperties<T>(_context);
            if (keyProperties.Count != 1)
            {
                return false;
            }

            // Shadow keys have no CLR member to read, which is a legitimate "no" rather than
            // an error — the caller asked whether the key is readable off the instance.
            PropertyInfo? clrProperty = keyProperties[0].PropertyInfo;
            if (clrProperty is null)
            {
                return false;
            }

            keyValue = clrProperty.GetValue(entity);
            return true;
        }

        #endregion
        #region "Staging (no save)"

        /// <inheritdoc />
        public async Task AddNoSaveAsync(T entity, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);

            // AddAsync rather than Add: only the async form can run a value generator that
            // needs to hit the database (HiLo, for instance) before the insert is staged.
            await _context.AddAsync(entity, cancellationToken);
        }

        /// <inheritdoc />
        public async Task AddRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entities);

            await _context.AddRangeAsync(entities, cancellationToken);
        }

        /// <inheritdoc />
        public Task UpdateNoSaveAsync(T entity, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            cancellationToken.ThrowIfCancellationRequested();

            _context.Update(entity);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entities);
            cancellationToken.ThrowIfCancellationRequested();

            _context.UpdateRange(entities);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveNoSaveAsync(T entity, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            cancellationToken.ThrowIfCancellationRequested();

            _context.Remove(entity);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entities);
            cancellationToken.ThrowIfCancellationRequested();

            _context.RemoveRange(entities);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _context.SaveChangesAsync(cancellationToken);

        #endregion
        #region "Async core operations"

        /// <inheritdoc />
        public async Task<int> CreateAsync(T entity, CancellationToken cancellationToken = default)
        {
            await AddNoSaveAsync(entity, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> CreateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            await AddRangeNoSaveAsync(entities, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            await UpdateNoSaveAsync(entity, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            await UpdateRangeNoSaveAsync(entities, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> HardDeleteAsync(T entity, CancellationToken cancellationToken = default)
        {
            await RemoveNoSaveAsync(entity, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> HardDeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            await RemoveRangeNoSaveAsync(entities, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        #endregion
    }
}

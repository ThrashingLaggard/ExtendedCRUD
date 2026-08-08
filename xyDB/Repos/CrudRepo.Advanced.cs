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
    public partial class CrudRepository<T> : ICrudQueryAccess<T>, ICrudKeyAccess<T>, ICrudStaging<T>, ICrudAsync<T>, ICrudSoftDelete<T> where T : class
    {
        /// <summary>
        /// How this repository closes a row when soft-deleting, or <see langword="null"/> when
        /// no marker was configured. See <see cref="CrudSoftDeleteOptions{T}"/>.
        /// </summary>
        private readonly CrudSoftDeleteOptions<T>? _softDeleteOptions;

        /// <summary>
        /// Creates a repository that can also soft-delete <typeparamref name="T"/> by stamping a
        /// configured marker property.
        /// </summary>
        /// <remarks>
        /// A separate constructor rather than an optional parameter on the primary one, so
        /// existing compiled consumers keep binding to the single-argument signature.
        /// </remarks>
        /// <param name="context">The caller-owned context, which this repository never owns.</param>
        /// <param name="softDeleteOptions">How to mark a row of <typeparamref name="T"/> as closed.</param>
        public CrudRepository(DbContext context, CrudSoftDeleteOptions<T>? softDeleteOptions) : this(context)
        {
            _softDeleteOptions = softDeleteOptions;
        }

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
        #region "Soft delete"

        /// <inheritdoc />
        public bool IsSoftDeleteConfigured => _softDeleteOptions is not null;

        /// <inheritdoc />
        public async Task<int> SoftDeleteAsync(T entity, CancellationToken cancellationToken = default)
        {
            StageMarker(entity, closed: true);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task SoftDeleteNoSaveAsync(T entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StageMarker(entity, closed: true);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SoftDeleteRangeNoSaveAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entities);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (T entity in entities)
            {
                StageMarker(entity, closed: true);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<int> RestoreAsync(T entity, CancellationToken cancellationToken = default)
        {
            StageMarker(entity, closed: false);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Writes the configured soft-delete marker onto <paramref name="entity"/> and marks
        /// <em>only</em> that property as modified.
        /// </summary>
        /// <remarks>
        /// Marking a single property, rather than calling <c>Update</c>, is the whole point: the
        /// resulting statement is <c>UPDATE … SET &lt;marker&gt; = … WHERE &lt;key&gt; = …</c>
        /// and touches nothing else. A whole-entity update would rewrite every column, which
        /// can overwrite a concurrent change to an unrelated field and makes the statement
        /// larger than the schema's <c>AFTER UPDATE OF &lt;marker&gt;</c> triggers need it to be.
        /// </remarks>
        /// <param name="entity">The entity whose marker to write.</param>
        /// <param name="closed"><see langword="true"/> to close the row, <see langword="false"/> to reopen it.</param>
        /// <exception cref="CrudConfigurationException">
        /// No soft-delete marker was configured, or the configured property is not mapped.
        /// </exception>
        private void StageMarker(T entity, bool closed)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (_softDeleteOptions is null)
            {
                throw new CrudConfigurationException(
                    $"No soft-delete marker is configured for '{typeof(T).Name}'. Construct the repository with a " +
                    $"{nameof(CrudSoftDeleteOptions<T>)}<{typeof(T).Name}> (for example " +
                    $"{nameof(CrudSoftDeleteOptions<T>)}<{typeof(T).Name}>.{nameof(CrudSoftDeleteOptions<T>.ClosedByTimestamp)}(e => e.ValidTo)), " +
                    "or use HardDeleteAsync if removing the row is what you meant.");
            }

            // Attach first when detached, so a caller can close a row it read with no-tracking
            // without having to reload it as tracked.
            EntityEntry<T> entry = _context.Entry(entity);
            if (entry.State == EntityState.Detached)
            {
                _context.Attach(entity);
                entry = _context.Entry(entity);
            }

            PropertyEntry marker;
            try
            {
                marker = entry.Property(_softDeleteOptions.MarkerPropertyName);
            }
            catch (InvalidOperationException ex)
            {
                throw new CrudConfigurationException(
                    $"The configured soft-delete marker '{typeof(T).Name}.{_softDeleteOptions.MarkerPropertyName}' is not a mapped " +
                    "property of the entity type in this DbContext's model.", ex);
            }

            marker.CurrentValue = closed ? _softDeleteOptions.CreateClosedValue() : _softDeleteOptions.CreateOpenValue();

            // Only meaningful for an entity that is already Unchanged/Modified. An Added entity
            // has no row to update yet, and forcing IsModified on it would throw.
            if (entry.State is EntityState.Unchanged or EntityState.Modified)
            {
                marker.IsModified = true;
            }
        }

        #endregion
    }
}

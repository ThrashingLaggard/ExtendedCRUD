using CRUD.Exceptions;
using CRUD.Interfaces;
using CRUD.Internal;
using CRUD.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using xyLogger.Loggers;


namespace CRUD.Repos
{
    /// <summary>
    /// Generic EF Core backed CRUD repository for any entity type <typeparamref name="T"/>.
    /// </summary>
    public partial class CrudRepository<T>(DbContext context) : IExtendedCrud<T> where T : class
    {
        // Statically typed on purpose: a dynamic DbContext defers member resolution to the DLR,
        // which hides typos/signature mismatches until runtime and disables IntelliSense/analyzers.
        /// <summary>
        /// The EF Core database context used to service all data access for <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// Supplied by the caller and owned by the caller. This repository never creates,
        /// caches beyond its own lifetime, or disposes a context, and never opens, commits or
        /// rolls back a transaction — work simply enlists in whatever transaction the supplied
        /// context already has. Construct one repository per unit of work around a context
        /// resolved from <c>IDbContextFactory&lt;TContext&gt;</c> or from a scoped registration;
        /// never register a repository as a singleton, which would capture one context forever.
        /// </remarks>
        private readonly DbContext _context = context;

        /// <summary>
        /// Upper bound for <see cref="Pageineering"/>'s <c>pageSize</c> and for <see cref="Find"/>'s
        /// result set, so a caller (or a malicious client) can't force an unbounded query against
        /// the database.
        /// </summary>
        private const int MaxPageSize = 500;

        /// <summary>
        /// Upper bound for how many entities <see cref="CreateRange"/>, <see cref="DeleteRange"/>,
        /// and <see cref="FindByIdsAsync"/> will act on in one call. Unlike <see cref="MaxPageSize"/>
        /// (which clamps, since truncating a read is harmless), an oversized batch is *rejected*
        /// here: silently dropping entities from a create/delete batch would mean data loss with
        /// no client-visible signal.
        /// </summary>
        private const int MaxBulkBatchSize = 500;

        #region "CRUD"
        /// <summary>
        /// Create a new entry in the database using the given data
        /// </summary>
        /// <param name="entity">The entity to persist.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the save operation.</param>
        /// <returns></returns>
        public async Task<bool> Create(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            bool isCreated = false;
            string invalidParam = "Please enter valid data for the creation!";
            string created = "Created and added the entry for the given data";

            // Guard clause: reject null input up front instead of letting it reach EF Core,
            // where it would surface as a less obvious ArgumentNullException from Add().
            // Reports failure via the bool return contract rather than throwing, so callers
            // that don't wrap every call in try/catch still get a safe, well-defined outcome.
            if (entity is null)
            {
                isCreated = false;
                await xyLog.AsxLog(invalidParam, LogLevel.Warning);
            }
            else
            {
                try
                {
                    // EntityEntry<T> derives from EntityEntry, so this pattern match still
                    // succeeds now that Add(...) resolves at compile time instead of via dynamic.
                    if (_context.Add(entity) is EntityEntry entityEntry)
                    {
                        await _context.SaveChangesAsync(cancellationToken);
                        await xyLog.AsxLog(created);
                        isCreated = true;
                    }
                }
                catch (Exception ex)
                {
                    await xyLog.AsxExLog(ex);
                }
            }
            return isCreated;
        }

        /// <summary>
        /// Get the corresponding instance for the given id
        /// </summary>
        /// <remarks>
        /// <para>
        /// Resolves through <c>DbContext.FindAsync</c>, which has one consequence that must be
        /// stated rather than discovered: it checks the change tracker before it queries. A
        /// filtered-out row is reported as "not found" when the lookup reaches the database, but
        /// an entity the context is <em>already tracking</em> is returned directly, without any
        /// global query filter being applied to it. For a soft-delete filter that means a row
        /// closed earlier in the same unit of work can still come back from this method.
        /// </para>
        /// <para>
        /// That behavior is preserved as-is because existing callers rely on it — notably on
        /// getting back an entity that has been added but not yet saved. Callers for whom a
        /// closed row must be indistinguishable from an absent one (a lifecycle that creates a
        /// fresh row when no live one exists, say, where reviving a closed row would be a
        /// correctness bug) should use
        /// <see cref="ICrudKeyAccess{T}.ReadByKeyAsync"/>, which always queries the database and
        /// always applies the model's filters.
        /// </para>
        /// <para>
        /// Use <see cref="ReadIncludingDeleted"/> to bypass the filters deliberately.
        /// </para>
        /// </remarks>
        /// <param name="id"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the lookup.</param>
        /// <returns></returns>
        public async Task<T?> Read(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            object[] param = { id };
            T? target = default;
            string nothingFound = $"Couldnt find the corresponding entries for the ID {id}";

            try
            {
                target = await _context.FindAsync<T>(param, cancellationToken);
                if (target is null)
                {
                    await xyLog.AsxLog(nothingFound);
                }
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }
            return target;
        }

        /// <summary>
        /// Updating the target entry
        /// </summary>
        /// <remarks>
        /// <see cref="DbUpdateConcurrencyException"/> is only ever thrown by EF Core when
        /// <typeparamref name="T"/> has a configured concurrency token (e.g. a
        /// <c>[Timestamp]</c>/<c>RowVersion</c> property, or a property configured via
        /// <c>IsConcurrencyToken()</c>). Without one, concurrent writes silently overwrite
        /// each other ("last write wins") per normal EF Core behavior — the 409/conflict
        /// handling in this method and in the optional controller only activates once the
        /// consumer's entity actually has a concurrency token. See the CRUD+ README
        /// "Concurrency" section.
        /// </remarks>
        /// <param name="entity"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the save operation.</param>
        /// <returns></returns>
        /// <exception cref="DbUpdateConcurrencyException">
        /// Thrown (not swallowed) when the update conflicts with a concurrent change, since the
        /// <see cref="Task{TResult}"/> of <see cref="bool"/> return contract can't distinguish
        /// "conflict" from "any other failure". Callers that need a specific response (e.g. the
        /// controller's 409 Conflict) should catch this explicitly.
        /// </exception>
        public async Task<bool> Update(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            bool isUpdated = false;

            if (entity is null)
            {
                return isUpdated;
            }
            try
            {
                _context.Update(entity);
                await _context.SaveChangesAsync(cancellationToken);
                isUpdated = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Specific-before-general: a concurrency conflict is meaningfully different from
                // any other failure and must propagate so the caller can react to it distinctly
                // (e.g. return 409 Conflict) instead of being flattened into "false".
                await xyLog.AsxExLog(ex, "Concurrency conflict while updating entity.", LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }
            return isUpdated;
        }

        /// <summary>
        /// Removing an entry from the database. If <paramref name="entity"/> implements
        /// <see cref="ISoftDeletable"/>, this flags it as deleted instead of removing the row;
        /// otherwise the row is hard-deleted, unchanged from the previous behavior.
        /// </summary>
        /// <remarks>
        /// Same concurrency-token prerequisite as <see cref="Update"/> applies here: without a
        /// configured concurrency token on <typeparamref name="T"/>, <see cref="DbUpdateConcurrencyException"/>
        /// is never thrown and a concurrent delete/modify is not detected. See the CRUD+ README
        /// "Concurrency" section.
        /// </remarks>
        /// <param name="entity"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the save operation.</param>
        /// <returns></returns>
        /// <exception cref="DbUpdateConcurrencyException">
        /// Thrown (not swallowed) when the delete conflicts with a concurrent change; see
        /// <see cref="Update"/> for the same rationale.
        /// </exception>
        public async Task<bool> Delete(T entity, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            string success = "Entry was removed";
            string failed = "Entry could not be removed";
            bool isDeleted = false;

            try
            {
                if (entity is ISoftDeletable softDeletable)
                {
                    // Opt-in soft delete: flag and save instead of removing the row. Whether
                    // this row then disappears from GetAll/Pageineering/Find is entirely up to
                    // the consumer's own HasQueryFilter configuration - see ISoftDeletable.
                    softDeletable.IsDeleted = true;
                    _context.Update(entity);
                }
                else
                {
                    _context.Remove(entity);
                }
                await _context.SaveChangesAsync(cancellationToken);
                isDeleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await xyLog.AsxExLog(ex, "Concurrency conflict while deleting entity.", LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                // Expected/handled failure path (e.g. missing row): logged here as an error entry.
                xyLog.ExLog(ex);
            }

            // Log success/failure based on the actual outcome, not unconditionally -
            // a caught exception above must never be reported as a successful delete.
            if (isDeleted)
            {
                await xyLog.AsxLog(success);
            }
            else
            {
                await xyLog.AsxLog(failed, LogLevel.Warning);
            }
            return isDeleted;
        }

        #endregion

        #region "Bulk"

        /// <summary>
        /// Creates multiple entities in a single database round-trip.
        /// </summary>
        /// <remarks>
        /// Rejects (returns <see langword="false"/> without touching the database) a batch
        /// larger than <see cref="MaxBulkBatchSize"/>, rather than silently truncating it —
        /// see <see cref="MaxBulkBatchSize"/> for why this differs from <see cref="Pageineering"/>'s
        /// clamp behavior.
        /// </remarks>
        /// <param name="entities">The entities to create.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public async Task<bool> CreateRange(IEnumerable<T> entities, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            bool isCreated = false;
            string invalidParam = "Please provide a non-empty collection of entities for bulk creation!";
            string created = "Created and added the entries for the given batch";

            // Materialize once: guards against a null sequence and against re-enumerating a
            // lazy/streaming source twice (Any() then AddRange()).
            List<T>? batch = entities?.ToList();

            if (batch is null || batch.Count == 0)
            {
                await xyLog.AsxLog(invalidParam, LogLevel.Warning);
                return isCreated;
            }

            if (batch.Count > MaxBulkBatchSize)
            {
                await xyLog.AsxLog($"Bulk create batch of {batch.Count} exceeds the maximum of {MaxBulkBatchSize}; rejecting.", LogLevel.Warning);
                return isCreated;
            }

            try
            {
                _context.AddRange(batch);
                // The entire point of this method: N entities, exactly one round-trip.
                await _context.SaveChangesAsync(cancellationToken);
                await xyLog.AsxLog(created);
                isCreated = true;
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return isCreated;
        }

        /// <summary>
        /// Deletes multiple entities in a single database round-trip.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike the single-entity <see cref="Delete"/>, this does not apply the
        /// <see cref="ISoftDeletable"/> convention — it always hard-deletes. Bulk soft-delete
        /// would need to flag and save every entity individually, defeating the single
        /// round-trip this method exists for; callers that need bulk soft-delete should flag
        /// the entities themselves and call <see cref="CreateRange"/>/<c>UpdateRange</c>-style
        /// batching directly against their own <c>DbContext</c>, or call <see cref="Delete"/>
        /// per entity.
        /// </para>
        /// <para>
        /// Rejects (returns <see langword="false"/> without touching the database) a batch
        /// larger than <see cref="MaxBulkBatchSize"/> — see that constant's remarks.
        /// </para>
        /// </remarks>
        /// <param name="entities">The entities to delete.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        /// <exception cref="DbUpdateConcurrencyException">
        /// Thrown (not swallowed) when the batch delete conflicts with a concurrent change; see
        /// <see cref="Update"/> for the same rationale.
        /// </exception>
        public async Task<bool> DeleteRange(IEnumerable<T> entities, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            bool isDeleted = false;
            string invalidParam = "Please provide a non-empty collection of entities for bulk deletion!";
            string deleted = "Removed the entries for the given batch";
            string failed = "Batch could not be removed";

            List<T>? batch = entities?.ToList();

            if (batch is null || batch.Count == 0)
            {
                await xyLog.AsxLog(invalidParam, LogLevel.Warning);
                return isDeleted;
            }

            if (batch.Count > MaxBulkBatchSize)
            {
                await xyLog.AsxLog($"Bulk delete batch of {batch.Count} exceeds the maximum of {MaxBulkBatchSize}; rejecting.", LogLevel.Warning);
                return isDeleted;
            }

            try
            {
                _context.RemoveRange(batch);
                await _context.SaveChangesAsync(cancellationToken);
                isDeleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await xyLog.AsxExLog(ex, "Concurrency conflict while bulk deleting entities.", LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }

            if (isDeleted)
            {
                await xyLog.AsxLog(deleted);
            }
            else
            {
                await xyLog.AsxLog(failed, LogLevel.Warning);
            }
            return isDeleted;
        }

        /// <summary>
        /// Looks up multiple entities by id in a single query, for bulk operations that need
        /// to resolve ids to entities without an N-query loop (e.g. the HTTP bulk-delete
        /// endpoint).
        /// </summary>
        /// <remarks>
        /// Respects any global query filter configured on <see cref="_context"/>, the same as
        /// <see cref="Read"/> — deliberately does not call <c>IgnoreQueryFilters()</c>. Rejects
        /// (returns an empty result without querying) an <paramref name="ids"/> batch larger
        /// than <see cref="MaxBulkBatchSize"/> — see that constant's remarks.
        /// </remarks>
        /// <param name="ids">The ids to look up.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the query.</param>
        /// <returns></returns>
        public async Task<IEnumerable<T>> FindByIdsAsync(IEnumerable<int> ids, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            IEnumerable<T> results = Enumerable.Empty<T>();
            string invalidParam = "Please provide a non-empty collection of ids for the bulk lookup!";

            List<int>? idList = ids?.ToList();
            if (idList is null || idList.Count == 0)
            {
                await xyLog.AsxLog(invalidParam, LogLevel.Warning);
                return results;
            }

            if (idList.Count > MaxBulkBatchSize)
            {
                await xyLog.AsxLog($"Bulk id lookup of {idList.Count} ids exceeds the maximum of {MaxBulkBatchSize}; rejecting.", LogLevel.Warning);
                return results;
            }

            try
            {
                // Key resolved from the model rather than assumed to be a property named "Id":
                // an entity keyed on NodeId/SectionId/a Guid/a string works here unchanged.
                Expression<Func<T, bool>> keysMatch =
                    CrudEntityKey.BuildKeyContainsPredicate<T>(_context, idList.Cast<object>());

                results = await _context.Set<T>()
                    .Where(keysMatch)
                    .ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return results;
        }

        #endregion

        #region "Extensions"
        /// <summary>
        /// Return an IEnumerable filled with the content of the target table
        /// </summary>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the query.</param>
        /// <returns></returns>
        public async Task<IEnumerable<T?>> GetAll([CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            string noEntrys = "No elements in list, please inform experts immedeately!";
            IEnumerable<T> inventory = [];

            try
            {
                inventory = await _context.Set<T>().ToListAsync(cancellationToken);

                if (!inventory.Any())
                {
                    await xyLog.AsxLog(noEntrys);
                }
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return inventory;
        }


        /// <summary>
        /// Return a EntityEntry instance for the given id, or <see langword="null"/> if no
        /// entity exists for that id.
        /// </summary>
        /// <remarks>
        /// The return type is nullable: when <see cref="Read"/> finds nothing for <paramref name="id"/>,
        /// there is no <see cref="EntityEntry"/> to hand back. Previously this method returned a
        /// null-forgiving (<c>!</c>) non-null-typed <see cref="EntityEntry"/> that was actually
        /// <see langword="null"/> at runtime — a latent bug for any caller trusting the
        /// non-nullable annotation. Callers must now null-check the result.
        /// </remarks>
        /// <param name="id"></param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the lookup.</param>
        /// <returns></returns>
        public async Task<EntityEntry?> GetEntryByID(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            EntityEntry? entity = null;
            T? target = await Read(id, callerName, cancellationToken);

            if (target is null)
            {
                return null;
            }

            try
            {
                entity = _context.Entry(target);
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return entity;
        }

        /// <summary>
        /// Checks whether an entity with the given id exists, without materializing it.
        /// </summary>
        /// <remarks>
        /// The key comes from the EF Core model, not from a property named "Id", so this works
        /// for any single-property primary key whatever it is called and whatever its type —
        /// <paramref name="id"/> is converted to the key's CLR type. Uses <c>AsNoTracking()</c>
        /// plus <c>AnyAsync</c> so no entity instance is ever materialized just to answer a
        /// yes/no question. Entity types with a composite key or no key raise
        /// <see cref="CrudKeyException"/>, which this method logs and reports as
        /// <see langword="false"/>; use <see cref="ICrudKeyAccess{T}.ExistsByKeyAsync"/> if the
        /// distinction between "absent" and "cannot be addressed by key" matters.
        /// </remarks>
        /// <param name="id">The id to check for.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public async Task<bool> ExistsAsync(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            bool exists = false;
            try
            {
                exists = await _context.Set<T>()
                    .AsNoTracking()
                    .AnyAsync(CrudEntityKey.BuildKeyEqualsPredicate<T>(_context, id), cancellationToken);
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return exists;
        }

        /// <summary>
        /// Reads an entity by id, deliberately bypassing any global EF Core query filter
        /// configured on <see cref="_context"/> (via <c>IgnoreQueryFilters()</c>).
        /// </summary>
        /// <remarks>
        /// This is the explicit "show it anyway" path for soft-deleted (or otherwise
        /// filtered-out) rows. <see cref="Read"/> stays filter-respecting and unchanged;
        /// use this method only where bypassing the consumer's filter is actually intended.
        /// </remarks>
        /// <param name="id">The id of the entity to fetch.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public async Task<T?> ReadIncludingDeleted(int id, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            T? target = default;
            string nothingFound = $"Couldnt find the corresponding entries for the ID {id} (including filtered-out rows)";

            try
            {
                target = await _context.Set<T>()
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(CrudEntityKey.BuildKeyEqualsPredicate<T>(_context, id), cancellationToken);
                if (target is null)
                {
                    await xyLog.AsxLog(nothingFound);
                }
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }
            return target;
        }


        /// <summary>
        /// Select which and how many elements to return
        /// </summary>
        /// <remarks>
        /// <para>
        /// When <paramref name="orderBy"/> is <see langword="null"/>, results are ordered by the
        /// entity's primary key as configured in the EF Core model — every component of it, in
        /// declaration order, for a composite key — instead of being left in an undefined order,
        /// so a page is stable across repeated calls.
        /// </para>
        /// <para>
        /// A keyless entity type has nothing to order by and is returned in provider order
        /// rather than being refused; pass an explicit <paramref name="orderBy"/> when paging
        /// one of those needs to be deterministic.
        /// </para>
        /// </remarks>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="callerName"></param>
        /// <param name="cancellationToken">Token used to cancel the query.</param>
        /// <param name="orderBy">Optional sort key selector. Falls back to ordering by "Id" when omitted.</param>
        /// <param name="descending">When <see langword="true"/>, sorts descending instead of ascending.</param>
        /// <returns></returns>
        public async Task<IEnumerable<T>> Pageineering(int page, int pageSize, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default, Expression<Func<T, object>>? orderBy = null, bool descending = false)
        {
            IEnumerable<T> paginatedList = Enumerable.Empty<T>();
            string noEntries = "No entries found for the specified page.";
            string wrongParams = "Page and pageSize must be greater than 0.";


            // Fail fast on invalid paging input: without this early return, a page < 1
            // produces a negative Skip() count, which EF Core rejects/mishandles depending
            // on provider - short-circuiting with the still-empty list is the safe, defined result.
            if (page < 1 || pageSize < 1)
            {
                await xyLog.AsxExLog(new ArgumentException(wrongParams), wrongParams, LogLevel.Warning);
                return paginatedList;
            }

            // Clamp rather than reject: a caller asking for too much still gets a usable,
            // capped result instead of an unbounded query hitting the database.
            if (pageSize > MaxPageSize)
            {
                await xyLog.AsxLog($"Requested pageSize {pageSize} exceeds the maximum of {MaxPageSize}; clamping.", LogLevel.Warning);
                pageSize = MaxPageSize;
            }

            try
            {
                // Calculate the number of items to skip
                int skip = (page - 1) * pageSize;

                // Get the total count of items
                int totalCount = await _context.Set<T>().CountAsync(cancellationToken);

                // Deterministic ordering is required before Skip/Take, otherwise the same
                // page can return different rows on repeated calls. Fall back to "Id" when
                // the caller didn't specify a sort key.
                IQueryable<T> query = _context.Set<T>();
                query = orderBy is not null
                    ? (descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy))
                    : CrudEntityKey.ApplyKeyOrder(query, _context, descending);

                // Fetch the paginated data
                paginatedList = await query.Skip(skip).Take(pageSize).ToListAsync(cancellationToken);


                // If still empty
                if (!paginatedList.Any())
                {
                    await xyLog.AsxLog(noEntries);
                }
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return paginatedList;
        }

        /// <summary>
        /// Returns entities matching <paramref name="predicate"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Read with <c>AsNoTracking()</c> and capped/ordered the same way as
        /// <see cref="Pageineering"/>'s fallback (ordered by the model's primary key, capped at
        /// <see cref="MaxPageSize"/>), so a broad predicate can't force an unbounded query.
        /// The cap is why this returns a materialized list; use
        /// <see cref="ICrudQueryAccess{T}.Query"/> when the result set should not be capped or
        /// should be projected before materializing.
        /// </para>
        /// <para>
        /// This overload takes a compiled <see cref="Expression{TDelegate}"/> from trusted,
        /// server-side calling code — it performs no property-level access control. The HTTP
        /// translation layer in CRUD+.AspNetCore (which builds a predicate from raw client
        /// input) is the one that checks <c>CrudNotFilterableAttribute</c>; this method does
        /// not, by design, since there is no client input here to restrict.
        /// </para>
        /// </remarks>
        /// <param name="predicate">The filter to apply.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        public async Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default)
        {
            IEnumerable<T> results = Enumerable.Empty<T>();
            string noMatches = "No entries matched the given predicate.";

            try
            {
                IQueryable<T> query = _context.Set<T>()
                    .AsNoTracking()
                    .Where(predicate);

                results = await CrudEntityKey.ApplyKeyOrder(query, _context)
                    .Take(MaxPageSize)
                    .ToListAsync(cancellationToken);

                if (!results.Any())
                {
                    await xyLog.AsxLog(noMatches);
                }
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return results;
        }

        #endregion
    }
}

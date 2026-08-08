using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Set-based writes: one statement over every row matching a predicate, instead of loading
    /// each row and saving it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For passes that are naturally set-shaped — re-flagging every row that now collides,
    /// clearing a flag from every row that no longer does, re-pointing a batch of references —
    /// the load-modify-save shape costs one round-trip per row and materializes entities nobody
    /// reads. These members issue a single <c>UPDATE … WHERE</c> or <c>DELETE … WHERE</c>.
    /// </para>
    /// <para>
    /// <b>They bypass the change tracker.</b> Entities already tracked by the context keep their
    /// stale in-memory values after one of these runs, and no <c>SaveChanges</c> interception,
    /// concurrency-token check, or tracked-entity side effect applies. Database-level triggers
    /// fire either way, since they are the database's, not EF Core's. Do not mix these with a
    /// transition the surrounding code is tracking through entities in the same unit of work —
    /// use the staging members for that.
    /// </para>
    /// <para>
    /// Both members save on their own — that is inherent to how the statements execute, not a
    /// choice this library makes. They still enlist in a transaction the caller has open, so
    /// they compose with the staged/flush pattern; they just cannot be deferred.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface ICrudSetOperations<T> where T : class
    {
        /// <summary>
        /// Updates every row matching <paramref name="predicate"/> in one statement.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The <paramref name="setters"/> parameter differs per target framework</b>, because
        /// EF Core itself changed this API: EF Core 8 takes an expression tree over
        /// <c>SetPropertyCalls&lt;T&gt;</c>, while EF Core 10 replaced that with an
        /// <c>Action&lt;UpdateSettersBuilder&lt;T&gt;&gt;</c> and removed the old type. Rather
        /// than invent a third setter syntax to paper over the difference, this exposes the
        /// native shape for whichever framework you compile against, so the compiler and the EF
        /// Core documentation both stay accurate. Only code that calls this member needs to
        /// care, and only if it multi-targets across both.
        /// </para>
        /// </remarks>
        /// <example>
        /// Clearing a flag from every row that no longer qualifies, on net8.0:
        /// <code>
        /// await crud.ExecuteUpdateAsync(
        ///     n => n.ParseError == "duplicate_id" &amp;&amp; !collidingIds.Contains(n.Id),
        ///     setters => setters.SetProperty(n => n.ParseError, (string?)null),
        ///     ct);
        /// </code>
        /// The same call on net10.0:
        /// <code>
        /// await crud.ExecuteUpdateAsync(
        ///     n => n.ParseError == "duplicate_id" &amp;&amp; !collidingIds.Contains(n.Id),
        ///     setters => setters.SetProperty(n => n.ParseError, (string?)null),
        ///     ct);
        /// </code>
        /// </example>
        /// <param name="predicate">Selects the rows to update.</param>
        /// <param name="setters">The property assignments to apply.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of rows updated.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="setters"/> is <see langword="null"/>.</exception>
        Task<int> ExecuteUpdateAsync(
            Expression<Func<T, bool>> predicate,
#if NET10_0_OR_GREATER
            Action<UpdateSettersBuilder<T>> setters,
#else
            Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> setters,
#endif
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes every row matching <paramref name="predicate"/> in one statement.
        /// </summary>
        /// <remarks>
        /// Always a real <c>DELETE</c>. Soft-delete configuration does not apply here — a
        /// predicate delete that quietly became an update would be the kind of surprise this
        /// library tries to avoid. To close rows in bulk instead, call
        /// <see cref="ExecuteUpdateAsync"/> against the marker property.
        /// </remarks>
        /// <param name="predicate">Selects the rows to delete.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The number of rows deleted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
        Task<int> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    }
}

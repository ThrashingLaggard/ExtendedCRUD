using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Predicate-based querying extension for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IQueryableCrudExtensions<T> where T : class
    {
        /// <summary>
        /// Returns entities matching <paramref name="predicate"/>.
        /// </summary>
        /// <remarks>
        /// <para>Results are read with <c>AsNoTracking()</c>, consistent with <see cref="IBaseCrudExtensions{T}.GetAll"/>
        /// and <see cref="IBaseCrudExtensions{T}.Pageineering"/> — this is a query operation, not intended
        /// to hand back entities ready for <see cref="IBaseCrud{T}.Update"/> in the same scope.</para>
        /// <para>Results are capped at the repository's <c>MaxPageSize</c> (same cap used by
        /// <c>Pageineering</c>) and ordered by the conventional <c>int</c> "Id" property before the
        /// cap is applied, so a broad predicate can't force an unbounded query and the truncation
        /// is deterministic rather than provider-dependent.</para>
        /// </remarks>
        /// <param name="predicate">The filter to apply.</param>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);
    }
}

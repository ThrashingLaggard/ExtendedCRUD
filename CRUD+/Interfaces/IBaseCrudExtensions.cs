using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Extension for the IBaseCrud interface
    /// providing :
    ///
    /// GetAll()
    ///
    /// Pageineering(x,y)
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IBaseCrudExtensions<T> where T : class
    {
        /// <summary>
        /// Get all instances
        /// </summary>
        /// <param name="callerName">Captured automatically; name of the calling member.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns></returns>
        Task<IEnumerable<T?>> GetAll([CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Filter the entries to be shown
        /// </summary>
        /// <remarks>
        /// <para>
        /// When <paramref name="orderBy"/> is <see langword="null"/>, results are ordered by the
        /// conventional <c>int</c> "Id" property (same convention as <see cref="IEfCoreCrudExtensions{T}.ExistsAsync"/>)
        /// instead of being left in an undefined order. This is a behavior change for existing
        /// callers: previously the page order was whatever the provider happened to return: now
        /// it is always deterministic and stable across repeated calls.
        /// </para>
        /// </remarks>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="callerName"></param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <param name="orderBy">Optional sort key selector. Falls back to ordering by "Id" when omitted.</param>
        /// <param name="descending">When <see langword="true"/>, sorts descending instead of ascending.</param>
        /// <returns></returns>
        Task<IEnumerable<T>> Pageineering(int page, int pageSize, [CallerMemberName] string? callerName = null, CancellationToken cancellationToken = default, Expression<Func<T, object>>? orderBy = null, bool descending = false);
    }
}

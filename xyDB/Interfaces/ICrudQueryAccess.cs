using System.Linq;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Direct access to the underlying <see cref="IQueryable{T}"/>, so anything this library
    /// does not wrap can still be expressed without abandoning it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A repository that only ever hands back a fully materialized <c>List&lt;T&gt;</c> forces
    /// callers into full-table loads and N+1 patterns the moment they need a projection, a join,
    /// an <c>Include</c>, or paging the wrapper does not cover. Exposing the queryable means the
    /// escape hatch is part of the design instead of a reason to bypass the package.
    /// </para>
    /// <para>
    /// Composing on the returned queryable is ordinary EF Core: project to a DTO with
    /// <c>Select</c>, page with <c>Skip</c>/<c>Take</c> before materializing, and call
    /// <c>ToListAsync</c> yourself with a cancellation token.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface ICrudQueryAccess<T> where T : class
    {
        /// <summary>
        /// Returns the queryable for <typeparamref name="T"/>, no-tracking and
        /// filter-respecting by default.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The defaults are the safe ones. Reads do not need tracked entities, and tracking them
        /// anyway both costs memory and risks a later <c>SaveChanges</c> writing something the
        /// caller only meant to read. Global query filters stay applied, which is what makes a
        /// soft-delete filter work without every call site remembering it.
        /// </para>
        /// <para>
        /// Both defaults are reversible, per call and in the open: pass
        /// <paramref name="tracked"/> for the update path that genuinely needs tracked entities,
        /// and <paramref name="ignoreQueryFilters"/> for the deliberate look at closed rows —
        /// historical comparison, an admin view, a test asserting a soft-deleted row still
        /// exists. No member of this library turns filters off behind the caller's back; the
        /// only members that ignore them are the ones that say so in their name or through this
        /// parameter.
        /// </para>
        /// </remarks>
        /// <param name="tracked">
        /// <see langword="true"/> to return tracked entities. Defaults to <see langword="false"/>.
        /// </param>
        /// <param name="ignoreQueryFilters">
        /// <see langword="true"/> to bypass every global query filter configured on the model —
        /// note this is all of them, not only a soft-delete filter. Defaults to <see langword="false"/>.
        /// </param>
        IQueryable<T> Query(bool tracked = false, bool ignoreQueryFilters = false);
    }
}

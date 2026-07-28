namespace CRUD.Models
{
    /// <summary>
    /// Minimal HTTP-safe filter for <c>CrudControllerAPI&lt;T&gt;</c>'s <c>find</c> endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An arbitrary <c>Expression&lt;Func&lt;T,bool&gt;&gt;</c> can't come from a query string or
    /// JSON body directly, so this DTO is the deliberately narrow translation layer: it supports
    /// exactly one operation — "does this named property equal this value" — resolved via
    /// reflection against the target entity type's declared properties at request time.
    /// </para>
    /// <para>
    /// This is intentionally not a general filter/query DSL (no ranges, no AND/OR composition,
    /// no nested paths). Broader filtering needs are expected to be served by callers using
    /// <c>IQueryableCrudExtensions&lt;T&gt;.Find</c> directly from server-side code, not over HTTP
    /// with an arbitrary predicate — the whole point of this DTO is to keep the HTTP surface to a
    /// safe, enumerable set of operations instead of accepting a client-supplied expression.
    /// </para>
    /// </remarks>
    public class CrudFilterRequest
    {
        /// <summary>The name of the property to filter on (case-insensitive).</summary>
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>
        /// The value to compare for equality, as its string representation. Converted to the
        /// target property's type before the comparison is built.
        /// </summary>
        public string? Value { get; set; }
    }
}

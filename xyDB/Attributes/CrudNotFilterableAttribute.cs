using System;

namespace CRUD.Attributes
{
    /// <summary>
    /// Marks a property as excluded from CRUD+.AspNetCore's HTTP <c>find</c> endpoint (which
    /// resolves <c>CrudFilterRequest.PropertyName</c> against any public property of the
    /// entity via reflection).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in the core <c>CRUD+</c> project (not <c>CRUD+.AspNetCore</c>) so entity authors
    /// can annotate their models without taking a dependency on the ASP.NET Core surface —
    /// only the HTTP translation layer in <c>CRUD+.AspNetCore</c> actually reads this attribute.
    /// </para>
    /// <para>
    /// This is an HTTP-layer-only restriction: <c>IQueryableCrudExtensions&lt;T&gt;.Find</c>
    /// itself (server-side code passing a compiled <c>Expression&lt;Func&lt;T,bool&gt;&gt;</c>
    /// directly) does not check this attribute and is unaffected — the predicate there comes
    /// from trusted code, not raw client input, so there is nothing to protect against at that
    /// layer. See the remarks on <c>CrudRepository&lt;T&gt;.Find</c>.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class CrudNotFilterableAttribute : Attribute
    {
    }
}

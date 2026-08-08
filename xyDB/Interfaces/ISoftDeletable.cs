namespace CRUD.Interfaces
{
    /// <summary>
    /// Opt-in marker for entities that should be soft-deleted (flagged, not removed) by
    /// <see cref="IBaseCrud{T}.Delete"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This library only sets <see cref="IsDeleted"/> when deleting an entity that implements
    /// this interface — it does <b>not</b> filter soft-deleted rows out of <c>GetAll</c>,
    /// <c>Pageineering</c>, or <c>Find</c>. Excluding them is the consuming application's
    /// responsibility via a global EF Core query filter on its own <c>DbContext</c>, e.g.:
    /// </para>
    /// <code>
    /// modelBuilder.Entity&lt;MyEntity&gt;().HasQueryFilter(e => !e.IsDeleted);
    /// </code>
    /// <para>
    /// This is the officially recommended EF Core pattern for soft delete and avoids this
    /// generic, DB-provider-agnostic library silently fighting a filter the consumer already
    /// configured (or forgot to configure) on the same entity.
    /// </para>
    /// <para>
    /// <see cref="IBaseCrud{T}.Read"/> stays filter-respecting: a soft-deleted entity is "not
    /// found" through the normal read path, exactly like a hard-deleted one, so ordinary
    /// callers (e.g. checking "does this still exist" before an update) aren't silently handed
    /// a row that's supposed to be gone. The library exposes a deliberately separate path,
    /// <c>IEfCoreCrudExtensions&lt;T&gt;.ReadIncludingDeleted</c>, for the explicit "show it
    /// anyway" case (admin views, recovery flows) — soft-deleted rows are only ever visible
    /// through that dedicated method, never as a side effect of a normal read.
    /// </para>
    /// </remarks>
    public interface ISoftDeletable
    {
        /// <summary>Whether this entity has been soft-deleted.</summary>
        bool IsDeleted { get; set; }
    }
}

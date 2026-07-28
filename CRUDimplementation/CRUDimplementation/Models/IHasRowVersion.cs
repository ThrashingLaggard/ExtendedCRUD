namespace CRUDimplementation.Models
{
    /// <summary>
    /// Marker for demo entities with a <c>[Timestamp]</c>-annotated concurrency token, so
    /// <see cref="CRUDimplementation.AppDbContext"/> can bump it manually on save. SQLite
    /// (unlike SQL Server) has no native auto-generated rowversion column type, so the
    /// "increment on every write" behavior that <c>[Timestamp]</c> gets for free on SQL
    /// Server has to be done by the application on this provider.
    /// </summary>
    public interface IHasRowVersion
    {
        byte[] RowVersion { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace CRUDimplementation.Models
{
    /// <summary>Plain demo entity — exercises the ordinary (non-soft-delete) CRUD+ path.</summary>
    public class Product : IHasRowVersion
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }

        /// <summary>
        /// Concurrency token so CRUD+'s DbUpdateConcurrencyException/409 handling is actually
        /// reachable in this demo — without one, EF Core never throws it (see CRUD+ README
        /// "Concurrency"). See AppDbContext for how this is bumped on SQLite, which (unlike SQL
        /// Server) has no native auto-generated rowversion column type.
        /// </summary>
        [Timestamp]
        public byte[] RowVersion { get; set; } = [];
    }
}

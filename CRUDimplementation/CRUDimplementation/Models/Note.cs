using CRUD.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace CRUDimplementation.Models
{
    /// <summary>
    /// Soft-deletable demo entity — exercises <see cref="ISoftDeletable"/>, the
    /// consumer-owned <c>HasQueryFilter</c> in <see cref="AppDbContext"/>, and
    /// <c>ReadIncludingDeleted</c>.
    /// </summary>
    public class Note : ISoftDeletable, IHasRowVersion
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }

        /// <summary>See <see cref="Product.RowVersion"/> for why this exists.</summary>
        [Timestamp]
        public byte[] RowVersion { get; set; } = [];
    }
}

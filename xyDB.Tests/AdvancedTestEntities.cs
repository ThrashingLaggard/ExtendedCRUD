using System.ComponentModel.DataAnnotations.Schema;

namespace xyDB.Tests
{
    /// <summary>
    /// The case the "a property named Id is the primary key" convention gets wrong: the key is
    /// <see cref="NodeId"/>, and this type <em>also</em> has a property called <see cref="Id"/>
    /// which is deliberately not the key, not unique, and nullable.
    /// </summary>
    /// <remarks>
    /// Two rows legitimately sharing the same <see cref="Id"/> is a supported state, so any code
    /// that mistook <see cref="Id"/> for the key would not merely read the wrong column — it
    /// would assume uniqueness that does not hold.
    /// </remarks>
    public class NodeEntity
    {
        /// <summary>The surrogate primary key.</summary>
        public int NodeId { get; set; }

        /// <summary>A non-unique business identifier. Explicitly not the primary key.</summary>
        public string? Id { get; set; }

        public string Path { get; set; } = string.Empty;

        public string? ParseError { get; set; }

        /// <summary>Soft-delete marker: <see langword="null"/> while the row is live.</summary>
        public DateTime? ValidTo { get; set; }

        /// <summary>
        /// Deliberately excluded from the model, so a soft-delete marker pointed at it can be
        /// shown to fail with a clear configuration error rather than silently doing nothing.
        /// </summary>
        [NotMapped]
        public bool Unmapped { get; set; }
    }

    /// <summary>Entity with a non-integer (<see cref="Guid"/>) primary key named <c>TagId</c>.</summary>
    public class TagEntity
    {
        public Guid TagId { get; set; }
        public int NodeId { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// Entity with a composite primary key, mapped so that constructing a repository over it
    /// must not throw even though it cannot be addressed by a single key value.
    /// </summary>
    public class CompositeKeyEntity
    {
        public int LeftId { get; set; }
        public int RightId { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Keyless entity type, mapped the way a database view or an as-yet-unused projection would
    /// be — configured via <c>HasNoKey()</c> in the context. Registering a repository over it
    /// must not throw either.
    /// </summary>
    public class KeylessEntity
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}

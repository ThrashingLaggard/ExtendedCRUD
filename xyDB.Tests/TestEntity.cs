using CRUD.Attributes;
using CRUD.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace xyDB.Tests
{
    public enum TestStatus
    {
        Active,
        Archived,
    }

    /// <summary>
    /// Entity exposing a <see cref="CrudNotFilterableAttribute"/>-marked property (for the
    /// HTTP find-exclusion test) plus a <see cref="Guid"/> and an <see cref="Enum"/> property
    /// (for the find-value-parsing tests).
    /// </summary>
    public class FilterableTestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid ExternalRef { get; set; }
        public TestStatus Status { get; set; }

        [CrudNotFilterable]
        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>Minimal entity used to exercise <c>CrudRepository&lt;T&gt;</c> against an in-memory database.</summary>
    public class TestEntity
    {
        public int Id { get; set; }

        // Configured as a concurrency token in TestDbContext.OnModelCreating, so tests can
        // provoke a real DbUpdateConcurrencyException instead of a hand-thrown stand-in.
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Entity implementing <see cref="ISoftDeletable"/>, used to exercise the soft-delete path.</summary>
    public class SoftDeletableTestEntity : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// Mirrors the CRUDimplementation sample host's concurrency-token pattern exactly
    /// (<c>[Timestamp] byte[] RowVersion</c>), so the "real [Timestamp] concurrency token"
    /// scenario from the hardening review is covered directly, alongside the existing
    /// <c>Name</c>-as-token coverage on <see cref="TestEntity"/>.
    /// </summary>
    public class RowVersionedTestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        [Timestamp]
        public byte[] RowVersion { get; set; } = [];
    }
}

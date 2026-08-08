using System;

namespace CRUD.Exceptions
{
    /// <summary>
    /// Thrown when a key-based operation cannot be carried out because the entity type's
    /// primary key, as configured in the EF Core model, does not support it.
    /// </summary>
    /// <remarks>
    /// <para>Raised for three distinct situations, distinguishable via <see cref="Reason"/>:</para>
    /// <list type="bullet">
    ///   <item><description>the type is not part of the context's model at all;</description></item>
    ///   <item><description>the type is mapped but keyless, so there is no key to look up by;</description></item>
    ///   <item><description>the type has a composite key, so a single scalar key value is not
    ///   enough to identify a row.</description></item>
    /// </list>
    /// <para>
    /// This is deliberately raised at call time, not at construction time: a repository over a
    /// keyless or composite-key entity is perfectly usable through every non-key-based member
    /// (predicate queries, the queryable hatch, staging, set-based operations), so refusing to
    /// construct one would make those entity types unusable for no reason.
    /// </para>
    /// </remarks>
    public class CrudKeyException : CrudException
    {
        /// <summary>Why the key-based operation could not be carried out.</summary>
        public CrudKeyProblem Reason { get; }

        /// <summary>The entity type whose key was inspected.</summary>
        public Type EntityType { get; }

        /// <summary>Creates a new <see cref="CrudKeyException"/>.</summary>
        /// <param name="entityType">The entity type whose key was inspected.</param>
        /// <param name="reason">Why the key-based operation could not be carried out.</param>
        /// <param name="message">Describes what failed.</param>
        public CrudKeyException(Type entityType, CrudKeyProblem reason, string message) : base(message)
        {
            EntityType = entityType;
            Reason = reason;
        }
    }

    /// <summary>Why a key-based operation was refused. See <see cref="CrudKeyException"/>.</summary>
    public enum CrudKeyProblem
    {
        /// <summary>The entity type is not mapped in the <c>DbContext</c>'s model.</summary>
        NotMapped,

        /// <summary>The entity type is mapped but has no primary key (a keyless entity type).</summary>
        Keyless,

        /// <summary>
        /// The entity type has a composite primary key, so it cannot be addressed by a single
        /// scalar key value.
        /// </summary>
        CompositeKey,

        /// <summary>
        /// The supplied key value could not be converted to the key property's CLR type.
        /// </summary>
        ValueNotConvertible,
    }
}

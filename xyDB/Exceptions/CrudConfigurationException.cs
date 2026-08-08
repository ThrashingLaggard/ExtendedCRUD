using System;

namespace CRUD.Exceptions
{
    /// <summary>
    /// Thrown when the repository was handed a configuration it cannot honor — for example a
    /// soft-delete marker selector that does not point at a mapped scalar property of the
    /// entity type.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="CrudKeyException"/> because the fix is different in kind: a
    /// key problem is a fact about the consumer's EF Core model, whereas this signals a
    /// mistake in what was passed to this library and is correctable at the call site.
    /// </remarks>
    public class CrudConfigurationException : CrudException
    {
        /// <summary>Creates a new <see cref="CrudConfigurationException"/>.</summary>
        /// <param name="message">Describes what was misconfigured.</param>
        public CrudConfigurationException(string message) : base(message)
        {
        }

        /// <summary>Creates a new <see cref="CrudConfigurationException"/> wrapping an underlying failure.</summary>
        /// <param name="message">Describes what was misconfigured.</param>
        /// <param name="innerException">The underlying failure.</param>
        public CrudConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

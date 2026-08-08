using System;

namespace CRUD.Exceptions
{
    /// <summary>
    /// Base type for every failure this library raises deliberately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists so consuming applications can translate data-access failures into their own
    /// error types by catching one base class, instead of either catching
    /// <see cref="Exception"/> wholesale or having to distinguish "the repository refused
    /// this" from "EF Core threw".
    /// </para>
    /// <para>
    /// This library never wraps <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>
    /// or <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> in a
    /// <see cref="CrudException"/> — those already carry provider detail a consumer needs and
    /// are let through unchanged. <see cref="CrudException"/> is for failures the library
    /// itself detects, such as an operation that cannot be expressed for the given entity type.
    /// </para>
    /// </remarks>
    public class CrudException : Exception
    {
        /// <summary>Creates a new <see cref="CrudException"/>.</summary>
        /// <param name="message">Describes what failed.</param>
        public CrudException(string message) : base(message)
        {
        }

        /// <summary>Creates a new <see cref="CrudException"/> wrapping an underlying failure.</summary>
        /// <param name="message">Describes what failed.</param>
        /// <param name="innerException">The underlying failure.</param>
        public CrudException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

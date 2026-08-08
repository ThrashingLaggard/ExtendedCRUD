using CRUD.Exceptions;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace CRUD.Options
{
    /// <summary>
    /// Describes how an entity type is soft-deleted: which property carries the marker, and
    /// what value marks a row as closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists because <see cref="Interfaces.ISoftDeletable"/> hardcodes both the shape
    /// (a <see cref="bool"/>) and the name (<c>IsDeleted</c>), which rules out the common
    /// temporal pattern where closing a row means stamping a timestamp column such as
    /// <c>valid_to</c>. Configuring the marker explicitly keeps the entity a plain POCO — no
    /// interface to implement, no property to rename.
    /// </para>
    /// <para>
    /// The marker property is what the repository writes and, crucially, the <em>only</em>
    /// property it marks as modified: the resulting statement is a plain
    /// <c>UPDATE … SET &lt;marker&gt; = …</c> touching no other column. That matters for schemas
    /// whose triggers are declared <c>AFTER UPDATE OF &lt;marker&gt;</c> — a whole-entity update
    /// would still fire them, but would also rewrite every other column for no reason and
    /// could clobber a concurrent change to an unrelated field.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type this configuration applies to.</typeparam>
    public sealed class CrudSoftDeleteOptions<T> where T : class
    {
        /// <summary>The name of the property carrying the soft-delete marker.</summary>
        public string MarkerPropertyName { get; }

        /// <summary>
        /// Produces the value written to <see cref="MarkerPropertyName"/> when closing a row.
        /// Invoked once per delete call, so a timestamp marker gets the time of the delete.
        /// </summary>
        private readonly Func<object?> _closedValueFactory;

        /// <summary>
        /// The value written to <see cref="MarkerPropertyName"/> when reopening a row, used by
        /// the restore path.
        /// </summary>
        private readonly Func<object?> _openValueFactory;

        private CrudSoftDeleteOptions(string markerPropertyName, Func<object?> closedValueFactory, Func<object?> openValueFactory)
        {
            MarkerPropertyName = markerPropertyName;
            _closedValueFactory = closedValueFactory;
            _openValueFactory = openValueFactory;
        }

        /// <summary>
        /// Configures soft delete against an arbitrary marker property.
        /// </summary>
        /// <typeparam name="TMarker">The marker property's type.</typeparam>
        /// <param name="markerSelector">Selects the marker property, e.g. <c>e =&gt; e.ValidTo</c>.</param>
        /// <param name="closedValue">Produces the value marking a row as deleted.</param>
        /// <param name="openValue">Produces the value marking a row as live again.</param>
        /// <exception cref="CrudConfigurationException">
        /// <paramref name="markerSelector"/> is not a simple property access on <typeparamref name="T"/>.
        /// </exception>
        public static CrudSoftDeleteOptions<T> MarkedBy<TMarker>(
            Expression<Func<T, TMarker>> markerSelector,
            Func<TMarker> closedValue,
            Func<TMarker> openValue)
        {
            ArgumentNullException.ThrowIfNull(markerSelector);
            ArgumentNullException.ThrowIfNull(closedValue);
            ArgumentNullException.ThrowIfNull(openValue);

            return new CrudSoftDeleteOptions<T>(
                ResolvePropertyName(markerSelector),
                () => closedValue(),
                () => openValue());
        }

        /// <summary>
        /// Configures the temporal soft-delete pattern: a nullable timestamp that is
        /// <see langword="null"/> while the row is live and stamped when it is closed.
        /// </summary>
        /// <remarks>
        /// The closing timestamp is taken as <see cref="DateTime.UtcNow"/> at the moment of the
        /// delete. Use <see cref="MarkedBy{TMarker}"/> to supply a different clock.
        /// </remarks>
        /// <param name="markerSelector">Selects the timestamp property, e.g. <c>e =&gt; e.ValidTo</c>.</param>
        public static CrudSoftDeleteOptions<T> ClosedByTimestamp(Expression<Func<T, DateTime?>> markerSelector) =>
            MarkedBy(markerSelector, () => DateTime.UtcNow, () => null);

        /// <summary>
        /// Configures the boolean soft-delete pattern, equivalent to what
        /// <see cref="Interfaces.ISoftDeletable"/> expresses but without requiring the entity to
        /// implement it or to name the property <c>IsDeleted</c>.
        /// </summary>
        /// <param name="markerSelector">Selects the flag property, e.g. <c>e =&gt; e.IsArchived</c>.</param>
        public static CrudSoftDeleteOptions<T> ClosedByFlag(Expression<Func<T, bool>> markerSelector) =>
            MarkedBy(markerSelector, () => true, () => false);

        /// <summary>The value to write when closing a row.</summary>
        internal object? CreateClosedValue() => _closedValueFactory();

        /// <summary>The value to write when reopening a row.</summary>
        internal object? CreateOpenValue() => _openValueFactory();

        /// <summary>
        /// Extracts the property name from the selector, unwrapping the boxing conversion the
        /// compiler inserts for value-typed properties.
        /// </summary>
        private static string ResolvePropertyName<TMarker>(Expression<Func<T, TMarker>> markerSelector)
        {
            Expression body = markerSelector.Body;
            if (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            {
                body = convert.Operand;
            }

            if (body is not MemberExpression { Member: PropertyInfo property } member || member.Expression != markerSelector.Parameters[0])
            {
                throw new CrudConfigurationException(
                    $"The soft-delete marker selector for '{typeof(T).Name}' must be a direct property access such as 'e => e.ValidTo', " +
                    $"but was '{markerSelector.Body}'.");
            }

            return property.Name;
        }
    }
}

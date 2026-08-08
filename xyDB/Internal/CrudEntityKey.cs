using CRUD.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CRUD.Internal
{
    /// <summary>
    /// Resolves an entity type's primary key from the EF Core model and builds the key-based
    /// expressions the repository needs (equality, set membership, deterministic ordering).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is the reason the repository no longer assumes a property literally named
    /// <c>Id</c> is the key. The key comes from
    /// <c>DbContext.Model.FindEntityType(typeof(T)).FindPrimaryKey()</c>, so an entity whose
    /// key is <c>NodeId</c>/<c>SectionId</c>/… works, and — critically — an entity that has a
    /// non-key property called <c>Id</c> (a business identifier, say) is never mistaken for a
    /// keyed one. There is no <c>IEntity&lt;TKey&gt;</c>-style constraint anywhere; entity
    /// types stay plain POCOs configured however the consumer prefers.
    /// </para>
    /// <para>
    /// Nothing here throws while merely inspecting a model. Composite-key and keyless entity
    /// types resolve fine; only an actual attempt to use a single scalar key against them
    /// raises <see cref="CrudKeyException"/>.
    /// </para>
    /// </remarks>
    internal static class CrudEntityKey
    {
        /// <summary>
        /// <c>EF.Property&lt;TProperty&gt;(object, string)</c>, used to address key properties by
        /// name in a way EF Core translates to SQL — this works for shadow properties and for
        /// backing-field-only keys, which a plain member access could not express.
        /// </summary>
        private static readonly MethodInfo EfPropertyMethod =
            typeof(EF).GetMethod(nameof(EF.Property), BindingFlags.Public | BindingFlags.Static)!;

        /// <summary>
        /// <c>Enumerable.Contains&lt;TSource&gt;(IEnumerable&lt;TSource&gt;, TSource)</c>, used to build the
        /// <c>keys.Contains(EF.Property&lt;TKey&gt;(e, name))</c> predicate behind bulk key lookups.
        /// </summary>
        private static readonly MethodInfo EnumerableContainsMethod =
            typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

        /// <summary>
        /// Returns the primary-key properties of <typeparamref name="T"/>, or an empty list if
        /// the type is unmapped or keyless. Never throws.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="context">The context whose model defines the entity type.</param>
        internal static IReadOnlyList<IProperty> GetKeyProperties<T>(DbContext context) where T : class
        {
            IEntityType? entityType = context.Model.FindEntityType(typeof(T));
            IKey? primaryKey = entityType?.FindPrimaryKey();
            return primaryKey?.Properties ?? (IReadOnlyList<IProperty>)Array.Empty<IProperty>();
        }

        /// <summary>
        /// Returns the single primary-key property of <typeparamref name="T"/>, or throws
        /// <see cref="CrudKeyException"/> explaining precisely why the type cannot be addressed
        /// by one scalar key value.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="context">The context whose model defines the entity type.</param>
        /// <exception cref="CrudKeyException">
        /// The type is unmapped, keyless, or has a composite key.
        /// </exception>
        internal static IProperty GetSingleKeyProperty<T>(DbContext context) where T : class
        {
            IEntityType? entityType = context.Model.FindEntityType(typeof(T));
            if (entityType is null)
            {
                throw new CrudKeyException(typeof(T), CrudKeyProblem.NotMapped,
                    $"'{typeof(T).Name}' is not mapped in the model of '{context.GetType().Name}', so it has no primary key to look up by. " +
                    "Map it via DbSet/IEntityTypeConfiguration, or use the predicate-based members instead.");
            }

            IKey? primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null)
            {
                throw new CrudKeyException(typeof(T), CrudKeyProblem.Keyless,
                    $"'{typeof(T).Name}' is a keyless entity type, so it cannot be addressed by key. " +
                    "Use the predicate-based members (Find/Query) instead.");
            }

            if (primaryKey.Properties.Count > 1)
            {
                string names = string.Join(", ", primaryKey.Properties.Select(p => p.Name));
                throw new CrudKeyException(typeof(T), CrudKeyProblem.CompositeKey,
                    $"'{typeof(T).Name}' has a composite primary key ({names}), so a single key value cannot identify a row. " +
                    "Use the key-values overload that accepts all key components, or the predicate-based members.");
            }

            return primaryKey.Properties[0];
        }

        /// <summary>
        /// Builds <c>e =&gt; EF.Property&lt;TKey&gt;(e, keyName) == keyValue</c> for the single-property
        /// primary key of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="context">The context whose model defines the entity type.</param>
        /// <param name="keyValue">The key value to match. Converted to the key's CLR type when needed.</param>
        /// <exception cref="CrudKeyException">
        /// The type cannot be addressed by a single key value, or <paramref name="keyValue"/> is
        /// not convertible to the key's CLR type.
        /// </exception>
        internal static Expression<Func<T, bool>> BuildKeyEqualsPredicate<T>(DbContext context, object keyValue) where T : class
        {
            IProperty keyProperty = GetSingleKeyProperty<T>(context);
            object converted = ConvertToKeyType<T>(keyValue, keyProperty);

            ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
            MethodCallExpression keyAccess = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(keyProperty.ClrType),
                parameter,
                Expression.Constant(keyProperty.Name));

            BinaryExpression equals = Expression.Equal(keyAccess, Expression.Constant(converted, keyProperty.ClrType));
            return Expression.Lambda<Func<T, bool>>(equals, parameter);
        }

        /// <summary>
        /// Builds <c>e =&gt; keyValues.Contains(EF.Property&lt;TKey&gt;(e, keyName))</c>, so a batch of
        /// keys resolves in one query rather than one query per key.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="context">The context whose model defines the entity type.</param>
        /// <param name="keyValues">The key values to match.</param>
        /// <exception cref="CrudKeyException">
        /// The type cannot be addressed by a single key value, or a value is not convertible to
        /// the key's CLR type.
        /// </exception>
        internal static Expression<Func<T, bool>> BuildKeyContainsPredicate<T>(DbContext context, IEnumerable keyValues) where T : class
        {
            IProperty keyProperty = GetSingleKeyProperty<T>(context);

            // A strongly typed list is required: EF Core translates Contains against
            // IEnumerable<TKey>, and an object-typed list would not match the key column.
            IList typedValues = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(keyProperty.ClrType))!;
            foreach (object? value in keyValues)
            {
                typedValues.Add(value is null ? null : ConvertToKeyType<T>(value, keyProperty));
            }

            ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
            MethodCallExpression keyAccess = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(keyProperty.ClrType),
                parameter,
                Expression.Constant(keyProperty.Name));

            MethodCallExpression contains = Expression.Call(
                EnumerableContainsMethod.MakeGenericMethod(keyProperty.ClrType),
                Expression.Constant(typedValues),
                keyAccess);

            return Expression.Lambda<Func<T, bool>>(contains, parameter);
        }

        /// <summary>
        /// Orders <paramref name="query"/> by the entity's primary key, so paging and capped
        /// reads are deterministic across repeated calls.
        /// </summary>
        /// <remarks>
        /// Composite keys are ordered by every key component in declaration order. A keyless or
        /// unmapped entity type is returned unordered rather than throwing: ordering exists here
        /// to make paging stable, and refusing to query at all would be a worse outcome than the
        /// undefined order such a type had before. Callers that need a guaranteed order for those
        /// types pass an explicit sort key.
        /// </remarks>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="query">The query to order.</param>
        /// <param name="context">The context whose model defines the entity type.</param>
        /// <param name="descending">Whether to sort descending.</param>
        internal static IQueryable<T> ApplyKeyOrder<T>(IQueryable<T> query, DbContext context, bool descending = false) where T : class
        {
            IReadOnlyList<IProperty> keyProperties = GetKeyProperties<T>(context);

            bool isFirst = true;
            foreach (IProperty keyProperty in keyProperties)
            {
                query = AppendOrderBy(query, keyProperty, descending, isFirst);
                isFirst = false;
            }
            return query;
        }

        /// <summary>Whether <typeparamref name="T"/> has a primary key this library can order by.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="context">The context whose model defines the entity type.</param>
        internal static bool HasKey<T>(DbContext context) where T : class => GetKeyProperties<T>(context).Count > 0;

        /// <summary>
        /// Appends one <c>OrderBy</c>/<c>ThenBy</c> clause for <paramref name="keyProperty"/>.
        /// </summary>
        /// <remarks>
        /// Built through the expression tree rather than the typed <c>Queryable</c> methods
        /// because the key's CLR type is only known at run time. <c>ThenBy</c> resolves because
        /// the preceding <c>OrderBy</c> call expression is already typed as
        /// <c>IOrderedQueryable&lt;T&gt;</c>.
        /// </remarks>
        private static IQueryable<T> AppendOrderBy<T>(IQueryable<T> query, IProperty keyProperty, bool descending, bool isFirst) where T : class
        {
            ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
            MethodCallExpression keyAccess = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(keyProperty.ClrType),
                parameter,
                Expression.Constant(keyProperty.Name));
            LambdaExpression selector = Expression.Lambda(keyAccess, parameter);

            string methodName = isFirst
                ? (descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy))
                : (descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy));

            MethodCallExpression orderedCall = Expression.Call(
                typeof(Queryable),
                methodName,
                [typeof(T), keyProperty.ClrType],
                query.Expression,
                Expression.Quote(selector));

            return query.Provider.CreateQuery<T>(orderedCall);
        }

        /// <summary>
        /// Coerces a caller-supplied key value to the key's CLR type, so an <c>int</c> passed for
        /// a <c>long</c> key (or a <c>string</c> for a <c>Guid</c> key) works instead of producing
        /// an opaque expression-tree type mismatch.
        /// </summary>
        private static object ConvertToKeyType<T>(object keyValue, IProperty keyProperty) where T : class
        {
            Type targetType = Nullable.GetUnderlyingType(keyProperty.ClrType) ?? keyProperty.ClrType;
            if (targetType.IsInstanceOfType(keyValue))
            {
                return keyValue;
            }

            try
            {
                if (targetType == typeof(Guid))
                {
                    return keyValue is string guidText ? Guid.Parse(guidText) : (Guid)keyValue;
                }
                if (targetType.IsEnum)
                {
                    return keyValue is string enumText ? Enum.Parse(targetType, enumText, ignoreCase: true) : Enum.ToObject(targetType, keyValue);
                }
                return Convert.ChangeType(keyValue, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                throw new CrudKeyException(typeof(T), CrudKeyProblem.ValueNotConvertible,
                    $"Key value '{keyValue}' ({keyValue.GetType().Name}) is not convertible to the key type " +
                    $"'{keyProperty.ClrType.Name}' of '{typeof(T).Name}.{keyProperty.Name}'.");
            }
        }
    }
}

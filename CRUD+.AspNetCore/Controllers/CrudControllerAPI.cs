using CRUD.Attributes;
using CRUD.Models;
using CRUD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using xyLogger.Loggers;


namespace CRUD.Controllers
{

    /// <summary>
    /// Generic REST controller exposing CRUD operations for entity type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authorization is policy-based rather than hardcoded to a specific role, so consuming
    /// applications decide who is allowed to read and who is allowed to write. Two policies
    /// must be registered by the host application:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>"CrudRead"</c> — required by <see cref="Get(System.Threading.CancellationToken)"/>, <see cref="Get(int, System.Threading.CancellationToken)"/>, and <see cref="Find"/>.</description></item>
    ///   <item><description><c>"CrudWrite"</c> — required by <see cref="Post"/>, <see cref="Put"/>, <see cref="Delete"/>, <see cref="PostRange"/>, and <see cref="DeleteRange"/>.</description></item>
    /// </list>
    /// <para>Register them in the consuming app's <c>Program.cs</c>, e.g.:</para>
    /// <code>
    /// builder.Services.AddAuthorization(options =>
    /// {
    ///     // Reproduces the previous default: any authenticated user may read.
    ///     options.AddPolicy("CrudRead", policy => policy.RequireAuthenticatedUser());
    ///
    ///     // Reproduces the previous default: only the "admin" role may write.
    ///     options.AddPolicy("CrudWrite", policy => policy.RequireRole("admin"));
    /// });
    /// </code>
    /// <para>
    /// Consumers that need per-entity-type granularity (e.g. a different policy per <typeparamref name="T"/>)
    /// can implement a custom <see cref="IAuthorizationHandler"/>/<see cref="IAuthorizationRequirement"/>
    /// that inspects the controller's resource/route data and register it under the same
    /// <c>"CrudRead"</c>/<c>"CrudWrite"</c> policy names, since ASP.NET Core policies may combine
    /// multiple requirements and handlers.
    /// </para>
    /// <para>
    /// <b>Bulk request bodies:</b> <see cref="PostRange"/>/<see cref="DeleteRange"/> reject a
    /// batch larger than the repository's internal cap with <c>400 Bad Request</c>, but that
    /// check only runs after the request body has already been read and deserialized. A client
    /// can still send an oversized body before that point. Configure
    /// <c>Kestrel:Limits:MaxRequestBodySize</c> (or a <c>[RequestSizeLimit]</c> attribute on
    /// your concrete controller subclass) to bound this at the host level — this is
    /// host-specific configuration the library itself can't impose.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type this controller exposes.</typeparam>
    [Route("api/[controller]")]
    [ApiController]
    public class CrudControllerAPI<T>(CrudService<T> crudService) : ControllerBase where T : class
    {
        // Injected via primary constructor; resolved per-request by DI (typically Scoped).
        private readonly CrudService<T> _service = crudService;

        // The library's id-based members (Read(int), GetEntryByID(int), Delete-by-id) already
        // assume entities expose an int primary key conventionally named "Id". Reused here,
        // via reflection, purely to detect a route/body id mismatch on PUT - if T doesn't
        // follow the convention, the property lookup below simply comes back null and the
        // consistency check is skipped rather than failing.
        private static readonly PropertyInfo? IdProperty = typeof(T).GetProperty("Id");

        /// <summary>
        /// Attempts to read an <c>int</c> "Id" value off <paramref name="entity"/> via the
        /// convention-based <see cref="IdProperty"/>.
        /// </summary>
        private static bool TryGetEntityId(T entity, out int id)
        {
            id = default;
            if (IdProperty is null || IdProperty.PropertyType != typeof(int))
            {
                return false;
            }
            if (IdProperty.GetValue(entity) is int value)
            {
                id = value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Builds an <c>e =&gt; e.[PropertyName] == Value</c> predicate for <see cref="Find"/> from
        /// the HTTP-safe <see cref="CrudFilterRequest"/> DTO, since an arbitrary
        /// <c>Expression&lt;Func&lt;T,bool&gt;&gt;</c> can't be bound from a request body directly.
        /// Only equality on a single named property is supported by design — see
        /// <see cref="CrudFilterRequest"/>.
        /// </summary>
        /// <remarks>
        /// Properties annotated with <see cref="CrudNotFilterableAttribute"/> are rejected with
        /// the exact same "unknown property" error as a genuinely nonexistent property name, so
        /// the response itself never reveals which properties exist but are excluded. This check
        /// applies only to this HTTP translation layer — <c>CrudRepository&lt;T&gt;.Find</c>,
        /// called directly with a compiled predicate from trusted server-side code, does not
        /// consult this attribute (there is no client input there to restrict).
        /// </remarks>
        private static bool TryBuildEqualsPredicate(CrudFilterRequest filter, out Expression<Func<T, bool>>? predicate, out string? error)
        {
            predicate = null;
            error = null;
            string unknownPropertyError = $"Unknown property '{filter.PropertyName}' on {typeof(T).Name}.";

            PropertyInfo? property = typeof(T).GetProperty(filter.PropertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || property.GetCustomAttribute<CrudNotFilterableAttribute>() is not null)
            {
                // Same error/shape whether the property doesn't exist or exists but is marked
                // [CrudNotFilterable] - the response must not leak which case applies.
                error = unknownPropertyError;
                return false;
            }

            object? typedValue;
            try
            {
                Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                if (filter.Value is null)
                {
                    typedValue = null;
                }
                else if (targetType == typeof(Guid))
                {
                    // Convert.ChangeType doesn't support Guid; parse explicitly instead of
                    // falling through to a misleading "not valid" error for a supported type.
                    if (!Guid.TryParse(filter.Value, out Guid guidValue))
                    {
                        error = $"Value '{filter.Value}' is not a valid Guid for property '{filter.PropertyName}'.";
                        return false;
                    }
                    typedValue = guidValue;
                }
                else if (targetType.IsEnum)
                {
                    // Same issue for enums: Convert.ChangeType only handles the underlying
                    // numeric representation, not the member name a client would actually send.
                    if (!Enum.TryParse(targetType, filter.Value, ignoreCase: true, out object? enumValue))
                    {
                        error = $"Value '{filter.Value}' is not a valid {targetType.Name} for property '{filter.PropertyName}'.";
                        return false;
                    }
                    typedValue = enumValue;
                }
                else
                {
                    typedValue = Convert.ChangeType(filter.Value, targetType, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                error = $"Value '{filter.Value}' is not valid for property '{filter.PropertyName}' of type {property.PropertyType.Name}.";
                return false;
            }

            ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
            MemberExpression propertyAccess = Expression.Property(parameter, property);
            ConstantExpression constant = Expression.Constant(typedValue, property.PropertyType);
            predicate = Expression.Lambda<Func<T, bool>>(Expression.Equal(propertyAccess, constant), parameter);
            return true;
        }

        /// <summary>
        /// Gets every entity of type <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>Requires the <c>"CrudRead"</c> authorization policy.</remarks>
        /// <param name="cancellationToken">Propagated to the underlying EF Core query so the client can abort the request.</param>
        /// <returns>200 OK with the full collection, or 500 (ProblemDetails) if the query fails.</returns>
        [HttpGet]
        [Authorize(Policy = "CrudRead")]
        public async Task<ActionResult<IEnumerable<T>>> Get(CancellationToken cancellationToken = default)
        {
            IEnumerable<T?> contents = [];
            try
            {
                contents = await _service.GetAll(cancellationToken: cancellationToken);
                return Ok(contents);
            }
            catch (Exception ex)
            {
                // Unexpected/unhandled failure: log at error level and report 500 rather than leak details to the caller.
                xyLog.ExLog(ex);
                return Problem(statusCode: 500);
            }
        }

        /// <summary>
        /// Gets a single entity of type <typeparamref name="T"/> by its id.
        /// </summary>
        /// <remarks>Requires the <c>"CrudRead"</c> authorization policy.</remarks>
        /// <param name="id">The primary key of the entity to fetch.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core lookup so the client can abort the request.</param>
        /// <returns>200 OK with the entity, 404 if no entity has that id, or 500 (ProblemDetails) if the lookup fails.</returns>
        [HttpGet("{id}")]
        [Authorize(Policy = "CrudRead")]
        public async Task<ActionResult<T>> Get(int id, CancellationToken cancellationToken = default)
        {
            T? target;
            string read = $"Entry with ID {id} was found: ";
            try
            {
                target = await _service.Read(id, cancellationToken: cancellationToken);
                xyLog.Log(read);
                return (target is not null) ? Ok(target) : StatusCode(404);
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
                return Problem(statusCode: 500);
            }
        }

        /// <summary>
        /// Returns entities of type <typeparamref name="T"/> matching a simple equality filter.
        /// </summary>
        /// <remarks>
        /// Requires the <c>"CrudRead"</c> authorization policy. Only single-property equality is
        /// supported over HTTP — see <see cref="CrudFilterRequest"/> for why an arbitrary predicate
        /// isn't accepted directly. Properties annotated with <c>CrudNotFilterableAttribute</c>
        /// are rejected the same way as an unknown property name. Results are capped and ordered
        /// the same way as <c>IQueryableCrudExtensions&lt;T&gt;.Find</c> itself (see that method's
        /// remarks).
        /// </remarks>
        /// <param name="filter">The property/value pair to filter on.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core query so the client can abort the request.</param>
        /// <returns>200 OK with the matches, 400 Bad Request for an unknown/excluded property or unconvertible value, or 500 (ProblemDetails) if the query fails.</returns>
        [HttpPost("find")]
        [Authorize(Policy = "CrudRead")]
        public async Task<ActionResult<IEnumerable<T>>> Find([FromBody] CrudFilterRequest filter, CancellationToken cancellationToken = default)
        {
            if (filter is null || string.IsNullOrWhiteSpace(filter.PropertyName))
            {
                return BadRequest("Request body must specify a PropertyName to filter on.");
            }

            if (!TryBuildEqualsPredicate(filter, out Expression<Func<T, bool>>? predicate, out string? error))
            {
                return BadRequest(error);
            }

            try
            {
                IEnumerable<T> results = await _service.Find(predicate!, cancellationToken: cancellationToken);
                return Ok(results);
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
                return Problem(statusCode: 500);
            }
        }

        /// <summary>
        /// Creates a new entity of type <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>Requires the <c>"CrudWrite"</c> authorization policy.</remarks>
        /// <param name="value">The entity to create, bound from the request body.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core save so the client can abort the request.</param>
        /// <returns>200 OK on success, 400 Bad Request when the body is missing/invalid, or 500 (ProblemDetails) if the write fails.</returns>
        [HttpPost]
        [Authorize(Policy = "CrudWrite")]
        public async Task<ActionResult> Post([FromBody] T value, CancellationToken cancellationToken = default)
        {
            string created = "Entry created successfully!";
            try
            {
                // A missing/unparsable body binds to null even for a generic reference type
                // parameter; guard explicitly instead of letting it reach the service layer.
                if (value is null)
                {
                    return BadRequest("Request body must contain a valid entity.");
                }

                // Reject invalid client input explicitly (400) instead of falling through to the generic 500 below.
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (await _service.Create(value, cancellationToken: cancellationToken))
                {
                    return Ok(created);
                }
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }
            return Problem(statusCode: 500);

        }

        /// <summary>
        /// Creates multiple entities of type <typeparamref name="T"/> in a single database round-trip.
        /// </summary>
        /// <remarks>
        /// Requires the <c>"CrudWrite"</c> authorization policy. The batch-size check runs here,
        /// before calling the service, so an oversized batch gets an immediate, specific 400
        /// rather than the repository's generic (and less informative) rejection.
        /// </remarks>
        /// <param name="values">The entities to create, bound from the request body.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core save so the client can abort the request.</param>
        /// <returns>200 OK on success, 400 Bad Request when the body is missing/empty/invalid/too large, or 500 (ProblemDetails) if the write fails.</returns>
        [HttpPost("bulk")]
        [Authorize(Policy = "CrudWrite")]
        public async Task<ActionResult> PostRange([FromBody] IEnumerable<T> values, CancellationToken cancellationToken = default)
        {
            string created = "Batch created successfully!";
            try
            {
                List<T>? batch = values?.ToList();
                if (batch is null || batch.Count == 0)
                {
                    return BadRequest("Request body must contain at least one entity.");
                }

                if (batch.Count > MaxBulkBatchSize)
                {
                    return BadRequest($"Request body contains {batch.Count} entities, which exceeds the maximum batch size of {MaxBulkBatchSize}.");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (await _service.CreateRange(batch, cancellationToken: cancellationToken))
                {
                    return Ok(created);
                }
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }
            return Problem(statusCode: 500);
        }

        /// <summary>
        /// Updates an existing entity of type <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>Requires the <c>"CrudWrite"</c> authorization policy.</remarks>
        /// <param name="id">The id of the entity being updated (route parameter; cross-checked against <paramref name="value"/> when <typeparamref name="T"/> exposes a conventional int "Id" property).</param>
        /// <param name="value">The updated entity data, bound from the request body.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core save so the client can abort the request.</param>
        /// <returns>200 OK on success, 400 Bad Request when the body is missing/invalid or its id doesn't match the route, 409 Conflict on a concurrent modification, or 500 (ProblemDetails) if the write fails.</returns>
        [HttpPut("{id}")]
        [Authorize(Policy = "CrudWrite")]
        public async Task<ActionResult> Put(int id, [FromBody] T value, CancellationToken cancellationToken = default)
        {
            string updated = "Entry updated successfully!";
            try
            {
                if (value is null)
                {
                    return BadRequest("Request body must contain a valid entity.");
                }

                // Same rationale as Post: surface validation problems as 400, not 500.
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Prevent a route id that silently disagrees with the body's id from updating
                // the wrong row (or masking a client bug). Skipped for entity types that don't
                // follow the library's int "Id" convention.
                if (TryGetEntityId(value, out int bodyId) && bodyId != id)
                {
                    return BadRequest($"Route id ({id}) does not match the entity id in the request body ({bodyId}).");
                }

                if (await _service.Update(value, cancellationToken: cancellationToken))
                {
                    return Ok(updated);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // Specific-before-general: a concurrency conflict gets its own, more informative
                // response instead of the generic 500 below. Only reachable if T has a configured
                // concurrency token - see CrudRepository<T>.Update's remarks.
                return Conflict("The entity was modified or deleted by another operation. Reload and try again.");
            }
            catch (Exception ex)
            {
                xyLog.ExLog(ex);
            }
            return Problem(statusCode: 500);
        }

        /// <summary>
        /// Deletes an entity of type <typeparamref name="T"/> by its id.
        /// </summary>
        /// <remarks>
        /// Requires the <c>"CrudWrite"</c> authorization policy. If <typeparamref name="T"/>
        /// implements <c>ISoftDeletable</c>, this flags the entity instead of removing the row —
        /// see <c>CrudRepository&lt;T&gt;.Delete</c>.
        /// </remarks>
        /// <param name="id">The primary key of the entity to delete.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core read/save so the client can abort the request.</param>
        /// <returns>200 OK on success, 404 if the entity was not found, 409 Conflict on a concurrent modification, or 500 (ProblemDetails) if the delete failed.</returns>
        [HttpDelete("{id}")]
        [Authorize(Policy = "CrudWrite")]
        public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            T? target;
            string removed = $"Entry with ID {id} was removed from database";
            try
            {
                // Delete-by-id requires a round-trip Read first because the repository's
                // Delete(T) overload needs a tracked/materialized entity, not a bare id.
                target = await _service.Read(id, cancellationToken: cancellationToken);
                if (target is null)
                {
                    return StatusCode(404);
                }

                if (await _service.Delete(target, cancellationToken: cancellationToken))
                {
                    return Ok(removed);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("The entity was modified or deleted by another operation. Reload and try again.");
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return Problem(statusCode: 500);
        }

        /// <summary>
        /// Deletes multiple entities of type <typeparamref name="T"/> by id, in a single database round-trip.
        /// </summary>
        /// <remarks>
        /// Requires the <c>"CrudWrite"</c> authorization policy. Ids that don't resolve to an
        /// existing entity are silently skipped (consistent with the single-entity <see cref="Delete"/>,
        /// which reports "not found" rather than failing the whole request over one bad id).
        /// Resolves ids to entities via a single <c>FindByIdsAsync</c> query instead of looping
        /// <c>Read</c> per id, so the "one round-trip" point of the underlying bulk delete isn't
        /// undermined by the read side.
        /// </remarks>
        /// <param name="ids">The ids of the entities to delete, bound from the request body.</param>
        /// <param name="cancellationToken">Propagated to the underlying EF Core reads/save so the client can abort the request.</param>
        /// <returns>200 OK on success, 400 Bad Request when the body is missing/empty/too large, 404 if none of the ids resolved to an entity, 409 Conflict on a concurrent modification, or 500 (ProblemDetails) if the delete failed.</returns>
        [HttpDelete("bulk")]
        [Authorize(Policy = "CrudWrite")]
        public async Task<ActionResult> DeleteRange([FromBody] IEnumerable<int> ids, CancellationToken cancellationToken = default)
        {
            string removed = "Batch removed successfully!";
            try
            {
                List<int>? idList = ids?.ToList();
                if (idList is null || idList.Count == 0)
                {
                    return BadRequest("Request body must contain at least one id.");
                }

                if (idList.Count > MaxBulkBatchSize)
                {
                    return BadRequest($"Request body contains {idList.Count} ids, which exceeds the maximum batch size of {MaxBulkBatchSize}.");
                }

                List<T> entities = (await _service.FindByIdsAsync(idList, cancellationToken: cancellationToken)).ToList();

                if (entities.Count == 0)
                {
                    return StatusCode(404);
                }

                if (await _service.DeleteRange(entities, cancellationToken: cancellationToken))
                {
                    return Ok(removed);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("One or more entities were modified or deleted by another operation. Reload and try again.");
            }
            catch (Exception ex)
            {
                await xyLog.AsxExLog(ex);
            }
            return Problem(statusCode: 500);
        }

        /// <summary>
        /// Mirrors <c>CrudRepository&lt;T&gt;.MaxBulkBatchSize</c> so the controller can reject
        /// an oversized bulk request before it ever reaches the service/repository layer.
        /// </summary>
        /// <remarks>
        /// Kept in sync manually with the repository's private constant of the same value —
        /// duplicated rather than shared because the repository's constant is intentionally
        /// private (an implementation detail), while the controller needs it to produce an
        /// early, specific 400 instead of relying on the repository's later, generic rejection.
        /// </remarks>
        private const int MaxBulkBatchSize = 500;
    }
}

# ExtendedCRUD

CRUD repository framework, designed for rapid development with [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/).

## Note:
Haters will say: 'Tis but a wrapper!
I will say: Indeed, most of it is. Now hush ye little scallywags.


## Key Feature

**Highly generic and composable CRUD repository pattern with rich extension points for Entity Framework Core.**  
This project allows you to quickly create repositories for any entity type, leveraging a clean interface structure and asynchronous patterns. You can easily extend, customize, and add advanced data access logic.


## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Entity Framework Core](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/)

The package targets **net8.0** and **net10.0**. Each target group references the matching EF
Core major version (8.0.x and 10.0.x), so a net10.0 application no longer takes a dependency
that only targets net8.0.

> **On the `SQLitePCLRaw` advisory (NU1903 / GHSA-2m69-gcr7-jv3q):** this package references
> only `Microsoft.EntityFrameworkCore` (plus `Microsoft.EntityFrameworkCore.Relational` on
> net8.0) and never a concrete database provider, so it does not pull `SQLitePCLRaw` in at all.
> If you see that advisory it comes from your own `Microsoft.EntityFrameworkCore.Sqlite`
> reference, and moving to EF Core 10 does **not** clear it on its own — EF Core 10.0.10's
> Sqlite provider still resolves `SQLitePCLRaw` 2.1.11. Pin it forward yourself:
> `<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />`.



## Usage Example

```csharp
// Example: Register and use the generic repository for your entity
public class User { /* ... */ }

// In your DI setup - scoped, never singleton: a repository holds the caller's DbContext.
services.AddScoped<IAdvancedCrud<User>, CrudRepository<User>>();

// Usage in your service:
public class UserService
{
    private readonly IAdvancedCrud<User> _repo;
    public UserService(IAdvancedCrud<User> repo) { _repo = repo; }

    public async Task CreateUser(User user, CancellationToken ct)
    {
        await _repo.CreateAsync(user, ct);
    }
}
```

`IAdvancedCrud<T>` is `IExtendedCrud<T>` plus everything added in 1.0.2. `IExtendedCrud<T>`
itself is unchanged, so existing code — including your own implementations of it, such as test
doubles — keeps compiling untouched.




## Primary keys come from your EF Core model

Nothing in this library assumes your key is an `int` property named `Id`. Keys are read from
`DbContext.Model.FindEntityType(typeof(T)).FindPrimaryKey()`, so a key called `NodeId`,
`SectionId` or `Slug` works as-is — there is no `IEntity<TKey>` interface to implement and no
naming convention to follow. Your entities stay plain POCOs.

This matters most when an entity has a property named `Id` that is *not* the key:

```csharp
public class NodeEntity
{
    public int NodeId { get; set; }      // the primary key
    public string? Id { get; set; }      // a business identifier - nullable, not unique
    public string Path { get; set; } = string.Empty;
}
```

`ReadByKeyAsync(2)` reads `NodeId`, and two rows sharing the same `Id` stay a perfectly legal
state.

```csharp
NodeEntity? node    = await repo.ReadByKeyAsync(42);                      // any key type
NodeEntity? closed  = await repo.ReadByKeyIncludingFilteredAsync(42);     // ignores filters
bool exists         = await repo.ExistsByKeyAsync(42);
IReadOnlyList<NodeEntity> many = await repo.ReadByKeysAsync([1, 2, 3]);   // one query
```

Key values are converted to the key's CLR type, so passing `"3f2b…"` for a `Guid` key or an
`int` for a `long` key works without pre-parsing.

**Composite-key and keyless entity types never fail at registration.** Constructing a
repository over them works, and every non-key member (`Find`, `Query`, `Pageineering`, staging,
set-based writes) works too. Only a call that genuinely needs one scalar key value throws
`CrudKeyException`, and its `Reason` says which case applies — `NotMapped`, `Keyless`,
`CompositeKey` or `ValueNotConvertible`.

## You own the DbContext, the transaction, and the flush

This library never creates, caches, disposes or replaces a `DbContext`, and never opens,
commits or rolls back a transaction. Operations enlist in whatever transaction the context you
passed already has.

For multi-phase writes — where intermediate states would violate a constraint and only you know
where the flush boundaries belong — use the `NoSave` members and save yourself:

```csharp
await using var db = await factory.CreateDbContextAsync(ct);
await using var tx = await db.Database.BeginTransactionAsync(ct);
var crud = new CrudRepository<SectionEntity>(db);

await crud.UpdateRangeNoSaveAsync(closedSections, ct);
await crud.SaveChangesAsync(ct);   // phase 1 boundary

await crud.UpdateRangeNoSaveAsync(vacatedSections, ct);
await crud.SaveChangesAsync(ct);   // phase 2 boundary

await crud.AddRangeNoSaveAsync(newSections, ct);
await crud.UpdateRangeNoSaveAsync(restoredSections, ct);
await crud.SaveChangesAsync(ct);   // phase 3 boundary

await tx.CommitAsync(ct);
```

Available as `AddNoSaveAsync`, `AddRangeNoSaveAsync`, `UpdateNoSaveAsync`,
`UpdateRangeNoSaveAsync`, `RemoveNoSaveAsync`, `RemoveRangeNoSaveAsync` and `SaveChangesAsync`.
The staging range members are not subject to the 500-item bulk cap, since that cap exists to
bound a single round-trip and staging performs none.

Repositories are cheap — construct one per unit of work around a context from
`IDbContextFactory<TContext>` (background work) or a scoped registration (per request). Do not
register one as a singleton; it would capture a single context forever.

## Dropping to `IQueryable<T>`

Anything this library does not wrap is still reachable, so you never have to bypass the package
to get a projection, an `Include`, or uncapped paging:

```csharp
List<NodeSummary> page = await repo.Query()
    .Where(n => n.Path.StartsWith("docs/"))
    .OrderBy(n => n.NodeId)
    .Skip(40).Take(20)
    .Select(n => new NodeSummary(n.NodeId, n.Path))
    .ToListAsync(ct);
```

`Query()` is no-tracking and filter-respecting by default. Both are reversible per call and in
the open: `Query(tracked: true)` for an update path, `Query(ignoreQueryFilters: true)` for a
deliberate look at filtered-out rows. No member of this library turns filters off behind your
back.

> One documented exception on the read side: the legacy `Read(id)` resolves through
> `DbContext.FindAsync`, which checks the change tracker before it queries — so an entity the
> context is *already tracking* comes back without global query filters being applied to it.
> That behavior is preserved for existing callers. If a soft-deleted row must be
> indistinguishable from an absent one, use `ReadByKeyAsync`, which always queries and always
> applies the model's filters.

## Set-based updates and deletes

For passes that are naturally set-shaped, one statement instead of N round-trips:

```csharp
// Flag every colliding row...
await repo.ExecuteUpdateAsync(
    n => collidingIds.Contains(n.Id),
    setters => setters.SetProperty(n => n.ParseError, "duplicate_id"), ct);

// ...and clear the flag from rows that no longer collide, making the pass idempotent.
await repo.ExecuteUpdateAsync(
    n => n.ParseError == "duplicate_id" && !collidingIds.Contains(n.Id),
    setters => setters.SetProperty(n => n.ParseError, (string?)null), ct);

await repo.ExecuteDeleteAsync(t => t.NodeId == nodeId, ct);   // e.g. rewrite a derived table
```

**These bypass the change tracker.** Already-tracked entities keep their stale in-memory values,
and no concurrency-token check or `SaveChanges` interception applies. Database triggers fire
either way, since they are the database's. They save on their own — inherent to how the
statements execute — but still enlist in a transaction you have open.

The `setters` parameter shape follows EF Core itself and therefore differs per target
framework: net8.0 takes `Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>`, while
net10.0 takes `Action<UpdateSettersBuilder<T>>` (EF Core 10 removed `SetPropertyCalls`). A
`setters => setters.SetProperty(...)` lambda as above compiles against both.

## Errors

The `Async`-suffixed members report failure by throwing; nothing is swallowed and nothing is
returned as a silent `null`/`false`:

- `CrudException` — base type for failures this library raises deliberately.
- `CrudKeyException` — a key-based call against a keyless, composite-key, unmapped or
  unconvertible key. Carries `Reason` and `EntityType`.
- `CrudConfigurationException` — e.g. soft delete called without a configured marker.

EF Core's own `DbUpdateException`/`DbUpdateConcurrencyException` are **not** wrapped — they
already carry provider detail you need.

The original members (`Create`, `Update`, `Delete`, …) keep their existing log-and-return-`false`
contract unchanged, so existing code behaves exactly as before. New code should prefer the
`Async` members.

## Authorization

`CrudControllerAPI<T>` no longer hardcodes a role for reads or writes. Instead it uses two ASP.NET Core authorization policies that the consuming application must register:

- `CrudRead` — required by `GET` (list and by-id)
- `CrudWrite` — required by `POST`, `PUT`, `DELETE`

Registering them in the consuming app's `Program.cs`. To reproduce the library's previous default behavior (any authenticated user can read, only the `admin` role can write):

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CrudRead", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CrudWrite", policy => policy.RequireRole("admin"));
});
```

Defining stricter, looser, or per-entity-type policies (e.g. by writing a custom `IAuthorizationHandler`/`IAuthorizationRequirement` that inspects the route/resource and registering it under the same policy names) 

`CrudControllerAPI<T>.Find` accepts only single-property equality
(`{ "PropertyName": "Name", "Value": "Alpha" }`) — an arbitrary predicate can't be safely
bound from an HTTP request body, so broader filtering needs `IQueryableCrudExtensions<T>.Find`
called directly from server-side code instead.

**Excluding sensitive properties from `find`:** the HTTP `find` endpoint resolves
`PropertyName` against *any* public property of the entity by default, which can turn it into
a value/existence oracle for a sensitive field (e.g. a password hash, an internal flag). Mark
any property that shouldn't be filterable over HTTP with `[CrudNotFilterable]`:

```csharp
using CRUD.Attributes;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [CrudNotFilterable]
    public string PasswordHash { get; set; } = string.Empty;
}
```

A request filtering on an excluded property gets the exact same `400 Bad Request` "unknown
property" response as a genuinely nonexistent property name -> the response never reveals
which case applies. This restriction is HTTP-layer-only: `IQueryableCrudExtensions<T>.Find`
called directly from your own server-side code with a compiled predicate is unaffected, since
that predicate comes from trusted code, not raw client input.

## Bulk operations

`CreateRange`/`DeleteRange` issue exactly one `SaveChangesAsync` for the whole batch instead
of one per entity:

```csharp
await repo.CreateRange(newUsers);
await repo.DeleteRange(usersToRemove);
```

Exposed over HTTP as `POST api/[controller]/bulk` and `DELETE api/[controller]/bulk` (the
latter takes a JSON array of ids, resolved to entities via a single `FindByIdsAsync` query —
not a per-id `Read` loop), both under the `CrudWrite` policy.

Both bulk operations (and `FindByIdsAsync`) reject — rather than silently truncate — a batch
larger than 500 items, returning `400 Bad Request` from the HTTP endpoints. Rejecting instead
of clamping is deliberate here: silently dropping entities from a create/delete batch would be
data loss with no client-visible signal, unlike clamping a read page size.

Even with that check in place, a client can still send an oversized request *body* before the
count is ever inspected. Configure `Kestrel:Limits:MaxRequestBodySize` (or a
`[RequestSizeLimit]` attribute on your concrete controller subclass) to bound this at the host
level — this is host-specific configuration the library itself can't and shouldn't impose.

## Deterministic paging and sorting

`Pageineering` always returns a stable, defined order. Without an explicit `orderBy` it falls
back to the entity's primary key as configured in your model — every component of it, in
declaration order, for a composite key — instead of leaving the order up to the provider:

```csharp
await repo.Pageineering(page: 2, pageSize: 20, orderBy: u => u.LastName, descending: true);
```

A keyless entity type has nothing to order by, so it is returned in provider order rather than
being refused; pass an explicit `orderBy` when paging one of those needs to be deterministic.

## Concurrency

`Update`, `Delete`, and `DeleteRange` let `DbUpdateConcurrencyException` propagate instead of
flattening it into `false`. `CrudControllerAPI<T>.Put`/`Delete`/`DeleteRange` catch it
explicitly and return `409 Conflict` rather than a generic `500`.

**This only ever activates if your entity has a configured concurrency token.**
`DbUpdateConcurrencyException` is EF Core's mechanism for detecting a conflicting concurrent
write, and EF Core only checks for one when a property is marked as a concurrency token — a
`[Timestamp]`/`RowVersion` property, or any property configured via `.IsConcurrencyToken()`.
Without one, two concurrent updates to the same row silently overwrite each other
("last write wins") like normal EF Core behavior; the 409/conflict handling described above
simply never triggers, because EF Core never throws.

```csharp
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
```

**SQLite note:** unlike SQL Server, SQLite has no native auto-generated rowversion column
type. `[Timestamp]` alone marks the property as database-generated, which on SQLite means
every insert fails with a `NOT NULL constraint` violation instead of ever reaching the
concurrency check. If you're on SQLite, configure the property as *not* store-generated and
bump its value yourself on every save:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>().Property(u => u.RowVersion).ValueGeneratedNever();
}

public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
{
    foreach (var entry in ChangeTracker.Entries<User>())
    {
        if (entry.State is EntityState.Added or EntityState.Modified)
        {
            entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
        }
    }
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}
```

This exact pattern is used by the `CRUDimplementation` sample host (see `AppDbContext.cs`,
`Product.cs`, `Note.cs`) and was verified end-to-end against a real SQLite database — a
stale `PUT` genuinely returns `409 Conflict`, not just a theoretical code path.

## Soft delete

Soft and hard delete are both first-class, and the member name says which one happens —
`SoftDeleteAsync` always closes, `HardDeleteAsync` always removes the row, and neither is ever
reinterpreted as the other.

### Configuring the marker

You do not have to implement an interface or name a property `IsDeleted`. Configure whichever
property carries the marker:

```csharp
// Temporal pattern: null while live, stamped when closed.
var repo = new CrudRepository<NodeEntity>(
    db, CrudSoftDeleteOptions<NodeEntity>.ClosedByTimestamp(n => n.ValidTo));

// Boolean flag under any name.
CrudSoftDeleteOptions<Doc>.ClosedByFlag(d => d.IsArchived);

// Anything else - supply your own values, e.g. a custom clock.
CrudSoftDeleteOptions<Doc>.MarkedBy(d => d.State, () => DocState.Closed, () => DocState.Live);
```

```csharp
await repo.SoftDeleteAsync(node, ct);            // stamp and save
await repo.SoftDeleteNoSaveAsync(node, ct);      // stage only, you flush
await repo.SoftDeleteRangeNoSaveAsync(nodes, ct);
await repo.RestoreAsync(node, ct);               // reopen
```

The write marks **only** the marker property as modified, so the statement is a plain
`UPDATE … SET valid_to = … WHERE …` touching no other column. That keeps a concurrent change to
an unrelated field intact and matches what a schema's `AFTER UPDATE OF valid_to` trigger
expects. Entities read with no tracking are attached automatically, so you do not have to
reload them as tracked first.

Note that `ExecuteDeleteAsync` is always a real `DELETE` — soft-delete configuration does not
apply to it. To close rows in bulk, use `ExecuteUpdateAsync` against the marker property.

### The original `ISoftDeletable` path

Entities that implement `ISoftDeletable` (`bool IsDeleted { get; set; }`) are still flagged
instead of removed by `Delete`, unchanged. This library does **not** filter soft-deleted rows
out of `GetAll`/`Pageineering`/`Find` itself — configure that in your own `DbContext`:

```csharp
modelBuilder.Entity<MyEntity>().HasQueryFilter(e => !e.IsDeleted);
```

> **Correction to earlier documentation.** This section previously claimed that `Read(id)`
> "respects that filter like any other query". That is not accurate, and never was — it also
> describes 1.0.1 incorrectly, as verified by running the scenario against the published 1.0.1
> package. `Read(id)` resolves through `DbContext.FindAsync`, which checks the change tracker
> *before* it queries. When the query reaches the database the filter does apply, but an entity
> the context is **already tracking** is handed back directly, filter or no filter. So a row you
> soft-deleted earlier in the same unit of work will still come back from `Read(id)`.
>
> The behavior is left exactly as it is, because changing it would break callers who rely on
> `Read` returning not-yet-saved tracked entities — and because it is what the released version
> does. If you need a lookup where a closed row is indistinguishable from an absent one (e.g. a
> lifecycle that creates a fresh row when no live one exists, where reviving a closed row would
> be a correctness bug), use **`ReadByKeyAsync`**, which always queries and always applies the
> model's filters. Both behaviors are pinned by tests.

Use `IEfCoreCrudExtensions<T>.ReadIncludingDeleted(id)` when you deliberately need to see a
soft-deleted row (e.g. an admin "show deleted" view or a recovery flow) — note that it bypasses
*all* global query filters configured on the entity, not only a soft-delete one.

## Roadmap


- Extending this beautiful mess
- Adding some Documentation




- 
## Author

- [ThrashingLaggard](https://github.com/ThrashingLaggard)

---


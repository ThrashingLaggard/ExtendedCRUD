# CRUD+

CRUD repository framework, designed for rapid development with [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/).

## Note:
Haters will say: 'Tis but a wrapper!
I will say: Indeed, most of it is. Now hush ye little scallywags.


## Key Feature

**Highly generic and composable CRUD repository pattern with rich extension points for Entity Framework Core.**  
This project allows you to quickly create repositories for any entity type, leveraging a clean interface structure and asynchronous patterns. You can easily extend, customize, and add advanced data access logic.


## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Entity Framework Core](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/)



## Usage Example

```csharp
// Example: Register and use the generic repository for your entity
public class User { /* ... */ }

// In your DI setup:
services.AddScoped<IExtendedCrud<User>, CrudRepository<User>>();

// Usage in your service:
public class UserService
{
    private readonly IExtendedCrud<User> _repo;
    public UserService(IExtendedCrud<User> repo) { _repo = repo; }

    public async Task CreateUser(User user)
    {
        await _repo.Create(user);
    }
}
```




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

`Pageineering` now always returns a stable, defined order. Without an explicit `orderBy` it
falls back to ordering by the entity's conventional `int` "Id" property instead of leaving
the order up to the provider:

```csharp
await repo.Pageineering(page: 2, pageSize: 20, orderBy: u => u.LastName, descending: true);
```

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

Entities that implement `ISoftDeletable` (`bool IsDeleted { get; set; }`) are flagged instead
of removed by `Delete`. This library does **not** filter soft-deleted rows out of
`GetAll`/`Pageineering`/`Find` itself — configure that in your own `DbContext`:

```csharp
modelBuilder.Entity<MyEntity>().HasQueryFilter(e => !e.IsDeleted);
```

`Read(id)` respects that filter like any other query (a soft-deleted entity is "not found",
same as a hard-deleted one). Use `IEfCoreCrudExtensions<T>.ReadIncludingDeleted(id)` when you
deliberately need to see a soft-deleted row (e.g. an admin "show deleted" view or a recovery
flow) — note that it bypasses *all* global query filters configured on the entity, not only a
soft-delete one.

## Roadmap


- Extending this beautiful mess
- Adding some Documentation




- 
## Author

- [ThrashingLaggard](https://github.com/ThrashingLaggard)

---


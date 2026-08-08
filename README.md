# ExtendedCRUD

A generic CRUD repository for Entity Framework Core. It handles the boring data access stuff so you don't have to.

> Haters will say: 'Tis but a wrapper!
> I will say: Indeed, most of it is. Now hush ye little scallywags.

## Install

```bash
dotnet add package ExtendedCRUD
```

Works with .NET 8.0+ and EF Core.

## Getting Started

```csharp
services.AddScoped<IAdvancedCrud<User>, CrudRepository<User>>();

public class UserService
{
    private readonly IAdvancedCrud<User> _repo;
    public UserService(IAdvancedCrud<User> repo) => _repo = repo;

    public async Task CreateUser(User user, CancellationToken ct)
        => await _repo.CreateAsync(user, ct);
}
```

## What You Get

**Keys from your EF model.** No interfaces to implement, no naming conventions. If your entity has `NodeId` as the key, it just works:

```csharp
NodeEntity? node = await repo.ReadByKeyAsync(42);
IReadOnlyList<NodeEntity> many = await repo.ReadByKeysAsync([1, 2, 3]);
```

**Drop to IQueryable whenever you need it.** Projections, includes, uncapped paging—reach EF directly:

```csharp
List<NodeSummary> page = await repo.Query()
    .Where(n => n.Path.StartsWith("docs/"))
    .Select(n => new NodeSummary(n.NodeId, n.Path))
    .ToListAsync(ct);
```

**Set-based updates and deletes.** One database round-trip instead of N:

```csharp
await repo.ExecuteUpdateAsync(
    n => collidingIds.Contains(n.Id),
    setters => setters.SetProperty(n => n.ParseError, "duplicate_id"), ct);

await repo.ExecuteDeleteAsync(n => n.NodeId == nodeId, ct);
```

**Soft delete with your own marker.** Boolean flag, timestamp, or whatever fits your domain:

```csharp
var repo = new CrudRepository<NodeEntity>(
    db, CrudSoftDeleteOptions<NodeEntity>.ClosedByTimestamp(n => n.ValidTo));

await repo.SoftDeleteAsync(node, ct);
await repo.RestoreAsync(node, ct);
```

**Staging for multi-phase writes.** When intermediate states would violate constraints, use `NoSave` members and flush on your terms:

```csharp
await crud.UpdateRangeNoSaveAsync(phase1, ct);
await crud.SaveChangesAsync(ct);

await crud.UpdateRangeNoSaveAsync(phase2, ct);
await crud.SaveChangesAsync(ct);

await tx.CommitAsync(ct);
```

**Bulk create and delete.** Single `SaveChangesAsync` for the whole batch:

```csharp
await repo.CreateRange(newUsers);
await repo.DeleteRange(usersToRemove);
```

**Deterministic paging.** Defaults to primary key order if you don't specify a sort:

```csharp
await repo.Pageineering(page: 2, pageSize: 20, orderBy: u => u.LastName, descending: true);
```

**Error handling that doesn't hide failures.** Async members throw; sync members return false (legacy):

```csharp
try { await repo.CreateAsync(user, ct); }
catch (CrudException ex) { /* handle it */ }
```

**Authorization (for ASP.NET Core).** Register two policies; the controller respects them:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CrudRead", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CrudWrite", policy => policy.RequireRole("admin"));
});
```

---

[ThrashingLaggard](https://github.com/ThrashingLaggard)

using CRUD.Repos;
using CRUD.Services;
using CRUDimplementation;
using CRUDimplementation.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=crudplus-demo.db"));

// CrudRepository<T>'s constructor takes the base DbContext type (that's the whole point -
// the library is provider/context-agnostic), but AddDbContext<AppDbContext> only registers
// the concrete AppDbContext, not DbContext itself. Without this line, resolving
// CrudRepository<T> throws "Unable to resolve service for type 'DbContext'".
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

// CRUD+ ships open generic types (CrudRepository<T>, CrudService<T>) rather than a
// DI extension method, so the consuming app registers them itself, once, for any T:
// CrudControllerAPI<T> -> CrudService<T> -> CrudRepository<T> -> DbContext (above).
builder.Services.AddScoped(typeof(CrudRepository<>));
builder.Services.AddScoped(typeof(CrudService<>));

builder.Services
    .AddAuthentication(DemoAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthHandler>(DemoAuthHandler.SchemeName, options => { });

// Reproduces CRUD+'s documented default (see its README "Authorization" section):
// any authenticated user may read, only the "admin" role may write. DemoAuthHandler
// authenticates every request, so in practice: no header = read-only "user", add
// "X-Demo-Role: admin" to unlock writes.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CrudRead", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CrudWrite", policy => policy.RequireRole("admin"));
});

builder.Services.AddControllers();

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    // EnsureCreated() is a demo-only shortcut - a real application would use EF Core
    // migrations (dotnet ef migrations add / database update) instead.
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Text("""
    CRUD+ demo host is running.

    Endpoints (Product = plain entity, Note = ISoftDeletable entity):
      GET    /api/products            (CrudRead)
      GET    /api/products/{id}       (CrudRead)
      POST   /api/products/find       (CrudRead)  body: { "propertyName": "Name", "value": "Widget" }
      POST   /api/products            (CrudWrite) body: { "name": "Widget", "price": 9.99 }
      POST   /api/products/bulk       (CrudWrite) body: [ { "name": "A", "price": 1 }, { "name": "B", "price": 2 } ]
      PUT    /api/products/{id}       (CrudWrite)
      DELETE /api/products/{id}       (CrudWrite)
      DELETE /api/products/bulk       (CrudWrite) body: [1, 2, 3]

      Same shape under /api/notes, but DELETE soft-deletes (Note implements ISoftDeletable)
      and deleted notes vanish from GET /api/notes via AppDbContext's HasQueryFilter.

    Auth: every request is auto-authenticated as role "user" (CrudRead passes).
    Add header "X-Demo-Role: admin" to also pass CrudWrite (POST/PUT/DELETE).
    """));

app.Run();

using CRUD.Controllers;
using CRUD.Services;
using CRUDimplementation.Models;

namespace CRUDimplementation.Controllers
{
    /// <summary>
    /// Closes CRUD+'s generic <see cref="CrudControllerAPI{T}"/> for <see cref="Product"/>.
    /// ASP.NET Core's <c>[controller]</c> route token resolves from this class's own name
    /// ("Products"), giving <c>/api/products</c> without repeating the route template.
    /// </summary>
    public class ProductsController(CrudService<Product> crudService) : CrudControllerAPI<Product>(crudService)
    {
    }
}

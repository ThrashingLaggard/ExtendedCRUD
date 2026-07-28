using CRUD.Controllers;
using CRUD.Services;
using CRUDimplementation.Models;

namespace CRUDimplementation.Controllers
{
    /// <summary>
    /// Closes CRUD+'s generic <see cref="CrudControllerAPI{T}"/> for <see cref="Note"/>,
    /// giving <c>/api/notes</c>. <see cref="Note"/> implements <c>ISoftDeletable</c>, so
    /// <c>DELETE /api/notes/{id}</c> flags rather than removes, and deleted notes disappear
    /// from <c>GET /api/notes</c> via <see cref="AppDbContext"/>'s query filter.
    /// </summary>
    public class NotesController(CrudService<Note> crudService) : CrudControllerAPI<Note>(crudService)
    {
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CRUD.Interfaces
{
    /// <summary>
    /// Combining all the Crud Interfaces
    ///
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IExtendedCrud<T> : IBaseCrud<T>, IBaseCrudExtensions<T>, IEfCoreCrudExtensions<T>, IBulkCrudExtensions<T>, IQueryableCrudExtensions<T> where T: class
    {

        // [CallerMemberName] string? callerName = null
    }
}

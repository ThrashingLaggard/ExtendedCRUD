namespace CRUD.Interfaces
{
    /// <summary>
    /// Everything <see cref="IExtendedCrud{T}"/> offers, plus the capabilities added in 1.0.2:
    /// model-driven key access, no-save staging under a caller-owned transaction, set-based
    /// writes, the queryable escape hatch, configurable soft delete, and throwing
    /// <c>Async</c> operations.
    /// </summary>
    /// <remarks>
    /// Deliberately a new interface rather than extra members on <see cref="IExtendedCrud{T}"/>:
    /// adding members to a published interface breaks every consumer that implements it — a test
    /// double, for instance — and none of this is worth doing that for. Existing code keeps
    /// compiling against <see cref="IExtendedCrud{T}"/> unchanged; code that wants the new
    /// surface depends on this instead.
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface IAdvancedCrud<T> :
        IExtendedCrud<T>,
        ICrudAsync<T>,
        ICrudKeyAccess<T>,
        ICrudStaging<T>,
        ICrudSetOperations<T>,
        ICrudQueryAccess<T>,
        ICrudSoftDelete<T>
        where T : class
    {
    }
}

namespace EEMOCantilanSDS.Domain.Common
{
    /// <summary>
    /// Marks an entity as belonging to exactly one municipality (tenant). Applied to operational,
    /// tenant-scoped entities — NOT to <c>Municipality</c> itself (a municipality is not tenant-owned).
    ///
    /// <para>Isolation is enforced centrally, not per-query: <c>AppDbContext</c> adds an EF Core global
    /// query filter on <see cref="MunicipalityId"/> for every implementor, and a save-changes interceptor
    /// stamps the current tenant's id on inserts. This is the single systematic guarantee that one
    /// municipality can never read or write another's data (roadmap Phase 3).</para>
    /// </summary>
    public interface IMunicipalityOwned
    {
        Guid MunicipalityId { get; }
    }
}

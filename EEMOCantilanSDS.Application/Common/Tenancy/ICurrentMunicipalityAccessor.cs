namespace EEMOCantilanSDS.Application.Common.Tenancy;

/// <summary>
/// Supplies the municipality id the current context is scoped to, for the EF global query filter and
/// write-stamping. While the system is single-tenant it is the <b>default</b> municipality (Cantilan),
/// resolved once and cached. <see cref="MunicipalityId"/> is <see cref="System.Guid.Empty"/> until
/// resolved — in which case the tenant filter is a no-op (nothing is hidden), so the app and tests work
/// before the registry is available.
/// </summary>
public interface ICurrentMunicipalityAccessor
{
    Guid MunicipalityId { get; }

    /// <summary>Sets the resolved municipality id (ignored when empty). Called once at startup.</summary>
    void Set(Guid municipalityId);
}

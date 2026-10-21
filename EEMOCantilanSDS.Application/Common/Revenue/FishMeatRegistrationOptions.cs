namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Installation and Office-policy gates are separate. Neither is bound to configuration by default.</summary>
public sealed class FishMeatRegistrationOptions
{
    public bool RequestsInstalled { get; init; }
    // Null preserves existing explicit synthetic-test options; production defaults remain off.
    // The guarded working-copy activation names exactly one authorized municipality.
    public IReadOnlySet<Guid>? RequestTenants { get; init; }
    public bool RequestsAvailable(Guid tenant) => RequestsInstalled && tenant != Guid.Empty
        && (RequestTenants is null || RequestTenants.Contains(tenant));
    // Once accepted, effective dates must remain in force for those business dates; rollback is not policy erasure.
    public IReadOnlyDictionary<Guid, DateOnly> RegistrationRequiredFrom { get; init; } = new Dictionary<Guid, DateOnly>();
    public bool RequiresRegistration(Guid tenant, DateOnly date) => tenant != Guid.Empty
        && RegistrationRequiredFrom.TryGetValue(tenant, out var from) && date >= from;
}

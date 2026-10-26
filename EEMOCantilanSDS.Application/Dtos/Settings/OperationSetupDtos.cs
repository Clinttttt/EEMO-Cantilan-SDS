namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// Where one revenue operation stands for a municipality, derived from backend facts only (its listing row, its revenue source and policy, its assigned
/// collectors and what each collector's app is told). Never an assumption of the UI, and never an authorization: nothing here lets money be collected.
/// </summary>
public enum OperationSetupState
{
    /// <summary>Not in this municipality's directory. It can be added.</summary>
    Available = 0,
    /// <summary>Listed, its revenue source and an effective policy exist, but no collector is assigned yet, so nobody is asked to collect it.</summary>
    Added = 1,
    /// <summary>Listed, but something is missing: a revenue source, an effective policy, or what an assigned collector needs (see the gaps).</summary>
    SetupRequired = 2,
    /// <summary>At least one active assigned collector is told, by the same rules the Collector app uses, that it can be collected now.</summary>
    Ready = 3,
}

/// <param name="Gaps">Plain-language reasons the operation is not ready, in the order to fix them. Empty when Ready or Available.</param>
public sealed record OperationSetupDto(
    string OperationCode,
    string DisplayName,
    string GroupKey,
    OperationSetupState State,
    bool SourceExists,
    bool SourceRetired,
    bool PolicyEffective,
    int AssignedCollectors,
    int ReadyCollectors,
    IReadOnlyList<string> Gaps,
    long Version);

public sealed record OperationSetupOverviewDto(bool UsesConfiguration, IReadOnlyList<OperationSetupDto> Items);

/// <summary>The Head's request to add an operation to its directory, with an optional name of its own. Bound to the version it was looking at.</summary>
public sealed record AddTenantOperationRequest(string? LocalDisplayName = null, long? ExpectedVersion = null);

/// <param name="SourceCreated">True when a revenue source did not exist and was created, inactive of policy, so it reads Setup required. False when an existing source was reused as it is (including a retired one, which is never reactivated).</param>
public sealed record AddTenantOperationResultDto(
    TenantOperationSettingsDto Settings,
    bool AlreadyAdded,
    bool SourceCreated,
    bool SourceRetired);

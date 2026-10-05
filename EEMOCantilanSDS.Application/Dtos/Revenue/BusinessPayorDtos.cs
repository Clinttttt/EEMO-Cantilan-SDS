using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// One active occupancy and the Business Payor explicitly linked to it, if any. <see cref="ActualOccupant"/> is the historical
/// text the occupancy was recorded under; it is display evidence and is never used to decide who a Payor is.
/// </summary>
public sealed record PayorOccupancyDto(
    Guid ContractId,
    Guid StallId,
    string FacilityShortName,
    string StallNo,
    string ActualOccupant,
    string? NameOnContract,
    Guid? PayorId,
    string? PayorName);

/// <summary>An existing Business Payor and where it is already in use, so the office can tell two people of one name apart.</summary>
public sealed record PayorCandidateDto(
    Guid PayorId,
    string DisplayName,
    BusinessPayorKind Kind,
    IReadOnlyList<string> Contexts);

/// <summary>Which occupancies to list.</summary>
public enum PayorLinkFilter
{
    NeedsPayor = 1,
    Linked = 2,
    All = 3
}

/// <summary>The office's explicit decision to link one occupancy to one existing Business Payor.</summary>
public sealed record LinkPayorRequest(Guid ContractId, Guid PayorId);

/// <summary>
/// The office's explicit decision to create a Business Payor for one occupancy and link it. A Payor with the same name is
/// never reused silently: when one exists the request is refused unless <see cref="ConfirmDuplicate"/> says the office
/// means a different person.
/// </summary>
public sealed record CreatePayorAndLinkRequest(
    Guid ContractId,
    string DisplayName,
    BusinessPayorKind Kind,
    bool ConfirmDuplicate = false);

public sealed record PayorLinkOutcomeDto(Guid ContractId, Guid PayorId, string PayorName, bool CreatedPayor);

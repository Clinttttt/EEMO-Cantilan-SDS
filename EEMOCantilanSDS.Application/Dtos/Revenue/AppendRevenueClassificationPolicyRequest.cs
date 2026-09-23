using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>Client-supplied policy values; classification ownership comes from the route and authenticated tenant.</summary>
public sealed record AppendRevenueClassificationPolicyRequest(
    DateOnly EffectiveDate,
    string DisplayName,
    string? Description,
    RevenueInstrumentType? PermittedInstrumentType);

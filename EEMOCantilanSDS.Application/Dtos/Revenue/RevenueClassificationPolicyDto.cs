using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>A single immutable, effective-dated presentation/instrument policy version.</summary>
public sealed record RevenueClassificationPolicyDto(
    Guid PolicyId,
    DateOnly EffectiveDate,
    string DisplayName,
    string? Description,
    RevenueInstrumentType? PermittedInstrumentType,
    DateTime CreatedAt,
    string? CreatedBy);

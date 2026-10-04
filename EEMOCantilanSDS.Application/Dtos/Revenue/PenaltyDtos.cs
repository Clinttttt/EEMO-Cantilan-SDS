using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>The current version of one approved penalty (IA-049), as of today.</summary>
public sealed record PenaltyDefinitionDto(
    Guid VersionId, string Code, string DisplayName, string? AppliesTo, GovernedServiceBasis Basis,
    decimal? FixedAmount, decimal? MaximumAmount, bool IsActive, DateOnly EffectiveDate);

/// <summary>A new version of a penalty. It never edits history; a new code creates a new penalty.</summary>
public sealed record DefinePenaltyRequest(
    string Code, string DisplayName, string? AppliesTo, DateOnly EffectiveDate, GovernedServiceBasis Basis,
    decimal? FixedAmount, decimal? MaximumAmount, bool IsActive);

/// <summary>
/// Adds one approved penalty to the caller's current Official Receipt draft. The client names the penalty; the server
/// resolves the version in force, and the amount is validated against it (a fixed penalty must equal its approved amount).
/// </summary>
public sealed record AddPenaltyDraftLineRequest(
    string PenaltyCode, decimal ProposedAmount, string? Origin, long ExpectedRevision);

/// <summary>One posted fine, as recorded in the canonical Collection.</summary>
public sealed record PenaltyRegisterRowDto(
    Guid CollectionId, DateOnly BusinessDate, DateTime RecordedAtUtc, string? PayerName, string? Origin,
    string PenaltyCode, string PenaltyName, string ReferenceCode, decimal Amount, string Status);

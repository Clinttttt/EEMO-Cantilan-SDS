using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record BillingPolicyVersionDto(Guid Id, DateOnly EffectiveFrom, BillingPolicy Rules, string? GoverningReference, DateTime RecordedAtUtc);

/// <summary>What an office bills by today. <see cref="InForce"/> is null while it still runs on the StallTrack defaults.</summary>
public sealed record BillingPolicyPageDto(BillingPolicy Current, BillingPolicyVersionDto? InForce, IReadOnlyList<BillingPolicyVersionDto> Versions, DateOnly Today);

public sealed record RecordBillingPolicyRequest(DateOnly EffectiveFrom, BillingPolicy Rules, string? GoverningReference);

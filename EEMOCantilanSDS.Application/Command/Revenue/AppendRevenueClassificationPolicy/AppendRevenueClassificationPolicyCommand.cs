using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy;

/// <summary>Appends an immutable policy version; it does not edit the classification identity or lifecycle.</summary>
public sealed record AppendRevenueClassificationPolicyCommand(
    Guid ClassificationId,
    DateOnly EffectiveDate,
    string DisplayName,
    string? Description,
    RevenueInstrumentType? PermittedInstrumentType)
    : IRequest<Result<RevenueClassificationPolicyDto>>;

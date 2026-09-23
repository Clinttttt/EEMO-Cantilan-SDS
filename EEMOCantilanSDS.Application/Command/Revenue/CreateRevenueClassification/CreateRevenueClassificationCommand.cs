using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;

/// <summary>Creates a caller-owned internal semantic identity and its first effective-dated policy.</summary>
public sealed record CreateRevenueClassificationCommand(
    string SemanticCode,
    string DisplayName,
    DateOnly EffectiveDate,
    string? Description,
    RevenueInstrumentType? PermittedInstrumentType)
    : IRequest<Result<RevenueClassificationDto>>;

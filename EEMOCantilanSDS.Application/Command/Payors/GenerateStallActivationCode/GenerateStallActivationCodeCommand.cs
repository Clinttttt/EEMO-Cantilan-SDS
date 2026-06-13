using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;

/// <summary>
/// Staff issue a single-use activation code for a stall, bound to the payor's contact number entered
/// at issuance. Any previous unredeemed code for the stall is voided.
/// </summary>
public record GenerateStallActivationCodeCommand(
    Guid StallId,
    string? ContactNumber,
    int? ValidityDays) : IRequest<Result<StallActivationCodeDto>>;

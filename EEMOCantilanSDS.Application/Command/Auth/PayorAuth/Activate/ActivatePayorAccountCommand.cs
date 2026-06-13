using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;

/// <summary>
/// Self-service activation: the payor proves ownership with a one-time activation code + their
/// registered contact number, then sets a password. On success they receive auth tokens and are
/// linked to the stall the code was issued for.
/// </summary>
public record ActivatePayorAccountCommand(
    string? ActivationCode,
    string? ContactNumber,
    string? FullName,
    string? Password) : IRequest<Result<TokenResponseDto>>;

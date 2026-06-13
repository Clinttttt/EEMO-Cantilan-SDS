using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;

/// <summary>Payor login using their registered contact number + password.</summary>
public record PayorLoginCommand(
    string? ContactNumber,
    string? Password) : IRequest<Result<TokenResponseDto>>;

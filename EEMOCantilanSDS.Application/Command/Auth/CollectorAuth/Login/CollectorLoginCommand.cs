using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;

public record CollectorLoginCommand(
    string? UsernameOrEmployeeId,
    string? Password) : IRequest<Result<TokenResponseDto>>;

using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;

// MunicipalityCode is the LGU the collector is signing into (optional). When supplied, the username/
// employee-id lookup is scoped to that municipality so a value shared across LGUs resolves to the correct
// tenant. When null/empty the lookup stays global — existing (Cantilan) clients are unchanged.
public record CollectorLoginCommand(
    string? UsernameOrEmployeeId,
    string? Password,
    string? MunicipalityCode = null) : IRequest<Result<TokenResponseDto>>;

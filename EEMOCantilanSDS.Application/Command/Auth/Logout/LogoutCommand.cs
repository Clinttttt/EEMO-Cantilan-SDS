using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.Logout;

public class LogoutCommand : IRequest<Result<bool>>
{
    public string RefreshToken { get; set; } = string.Empty;
}

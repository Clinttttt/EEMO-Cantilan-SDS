using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login
{
    // MunicipalityCode is the LGU the caller is signing into, carried from a scoped login URL (?lgu={code}).
    // Optional: when null/empty the login behaves exactly as before (no per-municipality boundary), so
    // existing callers and the default Cantilan flow are unchanged.
    public record LoginCommand(string? Username, string? Password, string? MunicipalityCode = null) : IRequest<Result<TokenResponseDto>>;
    
}

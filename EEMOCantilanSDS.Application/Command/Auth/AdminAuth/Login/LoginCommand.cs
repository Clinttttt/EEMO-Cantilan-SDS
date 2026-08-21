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
    //
    // RequirePlatformOperator is set by the ADMIN CONSOLE's own endpoint (api/adminauth/console-login). That console
    // belongs to the dedicated platform operator who onboards LGUs; it is not a second door into an LGU's portal. Without
    // it, any LGU administrator who knew the address could sign in there - and the office Head of the default LGU could,
    // because the platform-operator policy used to also count "SuperAdmin of the default municipality". That clause has
    // since been retired platform-wide, so the policy now agrees with this check rather than being broader than it.
    // The flag is still read directly here, because who may sign into the console is a question about the account.
    public record LoginCommand(
        string? Username,
        string? Password,
        string? MunicipalityCode = null,
        bool RequirePlatformOperator = false) : IRequest<Result<TokenResponseDto>>;
    
}

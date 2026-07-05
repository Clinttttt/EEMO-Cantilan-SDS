using EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Self-service branding for an LGU (post-activation). Restricted to the Head (SuperAdmin); edits the
/// caller's own municipality profile (office label, address, seal).
/// </summary>
[Authorize(Roles = "SuperAdmin")]
[Route("api/municipality-profile")]
[ApiController]
public class MunicipalityProfileController : ApiBaseController
{
    public MunicipalityProfileController(ISender sender) : base(sender)
    {
    }

    [HttpPut]
    public async Task<ActionResult<bool>> UpdateAsync([FromBody] UpdateOfficeProfileCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}

using EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile;
using EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetPaymentSettings;
using EEMOCantilanSDS.Application.Dtos.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Self-service branding for an LGU (post-activation). Restricted to the Head (SuperAdmin); edits the
/// caller's own municipality profile (office label, address, seal) and its online-payment account.
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

    /// <summary>Status of the LGU's online-payment account (never returns the secret).</summary>
    [HttpGet("payment")]
    public async Task<ActionResult<PaymentSettingsDto>> GetPaymentAsync()
    {
        var result = await Sender.Send(new GetMunicipalityPaymentSettingsQuery());
        return HandleResponse(result);
    }

    /// <summary>Set (or clear, when the secret is empty) the LGU's own PayMongo credentials.</summary>
    [HttpPut("payment")]
    public async Task<ActionResult<bool>> UpdatePaymentAsync([FromBody] SetMunicipalityPaymentCredentialsCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}

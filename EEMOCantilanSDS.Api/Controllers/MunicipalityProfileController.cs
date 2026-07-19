using EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile;
using EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;
using EEMOCantilanSDS.Application.Command.Municipalities.IssueMobileBindLink;
using EEMOCantilanSDS.Application.Queries.Auth.VerifyMyPassword;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetOfficeProfile;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetPaymentSettings;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

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
    private readonly IConfiguration _configuration;

    public MunicipalityProfileController(ISender sender, IConfiguration configuration) : base(sender)
    {
        _configuration = configuration;
    }

    [HttpPut]
    public async Task<ActionResult<bool>> UpdateAsync([FromBody] UpdateOfficeProfileCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>The caller Head's current office/LGU branding, to pre-fill the self-service edit form.</summary>
    [HttpGet("office")]
    public async Task<ActionResult<OfficeProfileEditDto>> GetOfficeAsync()
    {
        var result = await Sender.Send(new GetMyOfficeProfileQuery());
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

    /// <summary>Get (or rotate, with ?rotate=true) this LGU's collector-app bind link + the app download link.</summary>
    [HttpPost("bind-link")]
    public async Task<ActionResult<MobileBindLinkDto>> IssueBindLinkAsync([FromQuery] bool rotate = false)
    {
        var result = await Sender.Send(new IssueMobileBindLinkCommand(rotate));
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
            return HandleResponse(Result<MobileBindLinkDto>.Failure(
                result.Error ?? "Could not issue the bind link.", result.StatusCode ?? 400));

        var token = result.Value!;
        var appBase = (_configuration["Mobile:AppBaseUrl"] ?? "https://app.stalltrack.site").TrimEnd('/');
        var downloadUrl = _configuration["Mobile:DownloadUrl"] ?? $"{appBase}/download/stalltrack-collector-latest.apk";
        var dto = new MobileBindLinkDto(token, $"{appBase}/a/{token}", downloadUrl);
        return HandleResponse(Result<MobileBindLinkDto>.Success(dto));
    }

    /// <summary>Re-authentication: verify the current Head's own password before a sensitive change
    /// (e.g. opening the online-payment account configuration). Returns whether it matched.</summary>
    [HttpPost("verify-password")]
    public async Task<ActionResult<bool>> VerifyPasswordAsync([FromBody] VerifyMyPasswordRequest request)
    {
        var result = await Sender.Send(new VerifyMyPasswordQuery(request.Password));
        return HandleResponse(result);
    }
}

/// <summary>Request body for the re-authentication check.</summary>
public record VerifyMyPasswordRequest(string Password);

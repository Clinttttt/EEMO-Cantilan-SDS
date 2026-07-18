using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Anonymous collector-app version check. Config-driven (<c>Mobile:*</c> app settings) and fail-safe: with
/// nothing configured it reports version 1 / no minimum, so no update is ever prompted. Bump
/// <c>Mobile:LatestVersionCode</c> (and optionally <c>Mobile:MinSupportedVersionCode</c> to force) when a new
/// APK is published.
/// </summary>
[Route("api/mobile")]
[ApiController]
public class MobileVersionController(ISender sender, IConfiguration configuration) : ApiBaseController(sender)
{
    private readonly IConfiguration _configuration = configuration;

    [HttpGet("version")]
    [AllowAnonymous]
    public ActionResult<MobileAppVersionDto> GetVersion()
    {
        var latestCode = _configuration.GetValue<int?>("Mobile:LatestVersionCode") ?? 1;
        var latestName = _configuration["Mobile:LatestVersion"] ?? "1.0";
        var minCode = _configuration.GetValue<int?>("Mobile:MinSupportedVersionCode") ?? 0;
        var appBase = (_configuration["Mobile:AppBaseUrl"] ?? "https://app.stalltrack.site").TrimEnd('/');
        var apkUrl = _configuration["Mobile:DownloadUrl"] ?? $"{appBase}/download/stalltrack-collector-latest.apk";
        var notes = _configuration["Mobile:UpdateNotes"];

        var dto = new MobileAppVersionDto(
            latestCode, latestName, minCode, apkUrl,
            string.IsNullOrWhiteSpace(notes) ? null : notes);

        return HandleResponse(Result<MobileAppVersionDto>.Success(dto));
    }
}

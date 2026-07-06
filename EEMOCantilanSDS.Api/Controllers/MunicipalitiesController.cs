using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalities;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalityBranding;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

// Public, read-only registry for the CARCANMADCARLAN municipality selector (pre-login).
// Anonymous BY DESIGN — the public landing needs the list/status before any authentication,
// mirroring the existing public SetupController. Returns only non-sensitive presentation
// fields (no operational data).
[AllowAnonymous]
public class MunicipalitiesController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MunicipalityDto>>> GetMunicipalities()
    {
        var result = await Sender.Send(new GetMunicipalitiesQuery());
        return HandleResponse(result);
    }

    /// <summary>
    /// Public pre-login branding for a single LGU, resolved by subdomain identifier (its TenantCode or Code).
    /// Lets a subdomain's login page theme itself (office name, seal) before any authentication.
    /// </summary>
    [HttpGet("{identifier}/branding")]
    public async Task<ActionResult<MunicipalityBrandingDto>> GetBranding(string identifier)
    {
        var result = await Sender.Send(new GetMunicipalityBrandingQuery(identifier));
        return HandleResponse(result);
    }
}

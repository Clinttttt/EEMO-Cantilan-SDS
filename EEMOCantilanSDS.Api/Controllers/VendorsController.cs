using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Application.Queries.Vendors.GetVendorRegistry;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

public sealed class VendorsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("registry")]
    public async Task<ActionResult<VendorRegistryDto>> GetVendorRegistry(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var query = new GetVendorRegistryQuery(year, month);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }
}

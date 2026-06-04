using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "Collector")]
[Route("api/[controller]")]
[ApiController]
public class MobileController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("menu")]
    public async Task<ActionResult<MobileMenuDto>> GetMenuAsync()
    {
        var result = await Sender.Send(new GetCollectorMobileMenuQuery());
        return HandleResponse(result);
    }
}

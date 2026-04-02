using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Dtos;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminAuthController : ApiBaseController
    {
        public AdminAuthController(ISender sender) : base(sender)
        {
        }

        [HttpPost("login")]
        public async Task<ActionResult<TokenResponseDto>> LoginAsync([FromBody] LoginCommand request)
        {
            var result = await Sender.Send(request);
            return HandleResponse(result);
        }
    }
}

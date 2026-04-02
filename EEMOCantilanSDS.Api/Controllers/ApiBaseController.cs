using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens.Experimental;
using System.Security.Claims;

namespace EEMOCantilanSDS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public abstract class ApiBaseController : ControllerBase
    {
        private readonly ISender sender;
        public ApiBaseController(ISender sender) => this.sender = sender;
        protected ISender Sender => sender;

        protected ActionResult<T> HandleResponse<T>(Result<T> result)
        {
            return result.StatusCode switch
            {
                200 => Ok(result.Value),           
                201 => Created("", result.Value),  
                204 => NoContent(),               

                400 => result.ValidationErrors != null && result.ValidationErrors.Any()
                    ? BadRequest(new
                    {
                        IsSuccess = false,
                        Errors = result.ValidationErrors
                    })
                    : BadRequest(new
                    {
                        IsSuccess = false,
                        Error = result.Error
                    }),

                401 => Unauthorized(),
                403 => StatusCode(403),          
                404 => NotFound(),
                409 => Conflict(),
                500 => StatusCode(500, new
                {
                    IsSuccess = false,
                    Error = result.Error
                }),

                _ => BadRequest()
            };
        }

        public string UserId => User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        public string Role => User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? string.Empty; 
        public bool IsAuthenticated => User.Identity?.IsAuthenticated ?? false;
    }
}

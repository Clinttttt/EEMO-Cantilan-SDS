using MediatR;
using Microsoft.AspNetCore.Mvc;
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

        /// <summary>
        /// Turns a handler's outcome into a response. The ONLY place that decides what an outcome MEANS over HTTP: handlers
        /// state a <see cref="ResultStatus"/> — "already exists", "no such record", "not permitted" — and know nothing about
        /// status codes.
        ///
        /// <para>
        /// Which failures carry their message is deliberate. A 401 says nothing, so it cannot hint whether an account exists,
        /// and a 404 has nothing to add; the rest carry the sentence the office is meant to read. Every status here is read by
        /// something — the portal branches on Conflict to say a username is taken, and treats Unauthorized and Forbidden as
        /// "your session ended" rather than as an error to show — so <c>HandleResponseContractTests</c> holds this mapping.
        /// </para>
        /// </summary>
        protected ActionResult<T> HandleResponse<T>(Result<T> result)
        {
            return result.Status switch
            {
                ResultStatus.Ok => Ok(result.Value),
                ResultStatus.NoContent => NoContent(),

                ResultStatus.Invalid => result.ValidationErrors != null && result.ValidationErrors.Any()
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

                ResultStatus.Unauthorized => Unauthorized(),
                ResultStatus.Forbidden => StatusCode(403),
                ResultStatus.NotFound => NotFound(),

                // 423 Locked: a temporarily locked account. The body carries the message the sign-in page shows, which is
                // safe because this status is only returned once the password itself checked out — a wrong password still
                // gets a plain 401.
                ResultStatus.Locked => StatusCode(423, new
                {
                    IsSuccess = false,
                    Error = result.Error
                }),

                ResultStatus.Conflict => Conflict(new
                {
                    IsSuccess = false,
                    Error = result.Error
                }),

                ResultStatus.Failed => StatusCode(500, new
                {
                    IsSuccess = false,
                    Error = result.Error
                }),

                ResultStatus.UpstreamFailed => StatusCode(502, new
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

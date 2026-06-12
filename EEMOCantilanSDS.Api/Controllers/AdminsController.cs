using EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;
using EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;
using EEMOCantilanSDS.Application.Command.Admins.ToggleAdminStatus;
using EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Queries.Admins.GetAllAdmins;
using EEMOCantilanSDS.Application.Requests.Admins;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

// Account management is restricted to the Head (SuperAdmin), consistent with collector management.
[Authorize(Roles = "SuperAdmin")]
[Route("api/[controller]")]
[ApiController]
public class AdminsController : ApiBaseController
{
    public AdminsController(ISender sender) : base(sender)
    {
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminListDto>>> GetAllAdminsAsync()
    {
        var result = await Sender.Send(new GetAllAdminsQuery());
        return HandleResponse(result);
    }

    [HttpPost]
    public async Task<ActionResult<AdminDto>> CreateAdminAsync([FromBody] CreateAdminCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<bool>> UpdateAdminAsync(Guid id, [FromBody] UpdateAdminCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<bool>> ToggleStatusAsync(Guid id, [FromBody] ToggleAdminStatusRequest request)
    {
        var result = await Sender.Send(new ToggleAdminStatusCommand(id, request.IsActive));
        return HandleResponse(result);
    }

    [HttpPatch("{id:guid}/reset-password")]
    public async Task<ActionResult<bool>> ResetPasswordAsync(Guid id, [FromBody] ResetPasswordRequest request)
    {
        var result = await Sender.Send(new ResetAdminPasswordCommand(id, request.NewPassword, request.ConfirmPassword));
        return HandleResponse(result);
    }
}

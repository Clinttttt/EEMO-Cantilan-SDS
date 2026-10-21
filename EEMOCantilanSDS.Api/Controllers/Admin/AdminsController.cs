using EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;
using EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;
using EEMOCantilanSDS.Application.Command.Admins.ResendAdminInvitation;
using EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;
using EEMOCantilanSDS.Application.Command.Admins.RevokeAdminInvitation;
using EEMOCantilanSDS.Application.Command.Admins.SendAdminEmailVerification;
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

    /// <summary>
    /// (Re)sends an admin's email-confirmation link. Confirming the address is what lets that admin reset
    /// their own password later, so this is the Head's remedy when the first message was missed.
    /// </summary>
    [HttpPost("{id:guid}/send-email-verification")]
    public async Task<ActionResult<bool>> SendEmailVerificationAsync(Guid id)
    {
        var result = await Sender.Send(new SendAdminEmailVerificationCommand(id));
        return HandleResponse(result);
    }

    /// <summary>
    /// Invites an admin: the account is created inactive and its owner chooses their own password through a one-time emailed link.
    /// No password is issued or shared.
    /// </summary>
    [HttpPost("invitations")]
    public async Task<ActionResult<AdminInvitationDto>> InviteAdminAsync([FromBody] InviteAdminCommand command) =>
        HandleResponse(await Sender.Send(command));

    /// <summary>Issues a fresh link for an invitation not yet accepted; the previous link stops working.</summary>
    [HttpPost("{id:guid}/invitation/resend")]
    public async Task<ActionResult<AdminInvitationDto>> ResendInvitationAsync(Guid id) =>
        HandleResponse(await Sender.Send(new ResendAdminInvitationCommand(id)));

    /// <summary>Withdraws an invitation not yet accepted.</summary>
    [HttpPost("{id:guid}/invitation/revoke")]
    public async Task<ActionResult<bool>> RevokeInvitationAsync(Guid id) =>
        HandleResponse(await Sender.Send(new RevokeAdminInvitationCommand(id)));

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

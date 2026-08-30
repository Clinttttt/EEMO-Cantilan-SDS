using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.ApproveAssessmentRequest;
using EEMOCantilanSDS.Application.Command.Onboarding.DeclineAssessmentRequest;
using EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetAssessmentRequests;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// LGU onboarding — Stage 1 (Assessment). Public submission of an assessment request plus platform-operator
/// review (list / approve / decline). These records are pre-LGU and inert: nothing here writes live LGU data
/// — only Stage 4 (activation) does. Operator actions are additionally gated to the default-LGU SuperAdmin.
/// </summary>
[Route("api/assessment")]
[ApiController]
public class AssessmentController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Public assessment request submission from the landing site. Anonymous + rate-limited.</summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("auth")]
    [HttpPost("requests")]
    public async Task<ActionResult<AssessmentRequestDto>> Submit([FromBody] SubmitAssessmentRequestCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>Operator review queue — platform-operator only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<AssessmentRequestDto>>> GetAll()
    {
        var result = await Sender.Send(new GetAssessmentRequestsQuery());
        return HandleResponse(result);
    }

    /// <summary>Approve a pending request and record the onboarding link. Platform-operator only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("requests/{id:guid}/approve")]
    public async Task<ActionResult<AssessmentRequestDto>> Approve(Guid id, [FromBody] ApproveAssessmentRequestCommand command)
    {
        var result = await Sender.Send(command with { Id = id });
        return HandleResponse(result);
    }

    /// <summary>Decline a pending request with an optional notice. Platform-operator only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("requests/{id:guid}/decline")]
    public async Task<ActionResult<AssessmentRequestDto>> Decline(Guid id, [FromBody] DeclineAssessmentRequestCommand command)
    {
        var result = await Sender.Send(command with { Id = id });
        return HandleResponse(result);
    }
}

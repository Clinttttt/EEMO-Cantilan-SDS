using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.ApproveOnboardingValidation;
using EEMOCantilanSDS.Application.Command.Onboarding.ReturnOnboardingToDraft;
using EEMOCantilanSDS.Application.Command.Onboarding.SubmitOnboarding;
using EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraft;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraftByRequest;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// LGU onboarding — Stage 2 (config staging). The LGU loads/edits/submits its staged draft anonymously via
/// the secure token from its onboarding link; the operator reads a submitted draft for validation. Drafts are
/// pre-LGU and inert — nothing here writes live LGU data (only activation does).
/// </summary>
[Route("api/onboarding")]
[ApiController]
public class OnboardingController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Load an LGU's onboarding draft by its secure token. Anonymous + rate-limited.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpGet("{token}")]
    public async Task<ActionResult<OnboardingDraftDto>> GetDraft(string token)
    {
        var result = await Sender.Send(new GetOnboardingDraftQuery(token));
        return HandleResponse(result);
    }

    /// <summary>Save the LGU's edited configuration. Anonymous + rate-limited.</summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("auth")]
    [HttpPut("{token}")]
    public async Task<ActionResult<OnboardingDraftDto>> UpdateConfig(string token, [FromBody] UpdateOnboardingConfigRequest? body)
    {
        var result = await Sender.Send(new UpdateOnboardingConfigCommand(token, body?.ConfigJson));
        return HandleResponse(result);
    }

    /// <summary>Submit the LGU's onboarding draft for validation. Anonymous + rate-limited.</summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("auth")]
    [HttpPost("{token}/submit")]
    public async Task<ActionResult<OnboardingDraftDto>> Submit(string token)
    {
        var result = await Sender.Send(new SubmitOnboardingCommand(token));
        return HandleResponse(result);
    }

    /// <summary>Operator read of a submitted draft by its assessment request id. Platform-operator only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("by-request/{assessmentRequestId:guid}")]
    public async Task<ActionResult<OnboardingDraftDto>> GetByRequest(Guid assessmentRequestId)
    {
        var result = await Sender.Send(new GetOnboardingDraftByRequestQuery(assessmentRequestId));
        return HandleResponse(result);
    }

    /// <summary>Approve the validation dry-run — advances the request to Activation. Platform-operator only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("by-request/{assessmentRequestId:guid}/approve-validation")]
    public async Task<ActionResult<AssessmentRequestDto>> ApproveValidation(Guid assessmentRequestId)
    {
        var result = await Sender.Send(new ApproveOnboardingValidationCommand(assessmentRequestId));
        return HandleResponse(result);
    }

    /// <summary>Return the config for corrections — reopens the draft (Onboarding). Platform-operator only.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("by-request/{assessmentRequestId:guid}/return")]
    public async Task<ActionResult<AssessmentRequestDto>> ReturnToOnboarding(Guid assessmentRequestId, [FromBody] ReturnOnboardingRequestBody? body)
    {
        var result = await Sender.Send(new ReturnOnboardingToDraftCommand(assessmentRequestId, body?.Note));
        return HandleResponse(result);
    }
}

/// <summary>Optional body for the return-to-onboarding action.</summary>
public record ReturnOnboardingRequestBody(string? Note);

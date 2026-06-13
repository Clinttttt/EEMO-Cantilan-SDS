using System.Text;
using EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;
using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.OnlinePayments.GetAwaitingOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/onlinepayments")]
[ApiController]
public class OnlinePaymentsController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Payor starts an online payment for one of their payment records; returns a checkout URL.</summary>
    [HttpPost("initiate")]
    [Authorize(Roles = "Payor")]
    public async Task<ActionResult<InitiateOnlinePaymentResultDto>> InitiateAsync([FromBody] InitiateOnlinePaymentCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }

    /// <summary>
    /// PayMongo webhook. Anonymous, but authenticity is verified against the raw body + signature
    /// header inside the handler (fail-closed). The body is read raw — not model-bound — so the
    /// exact bytes are available for HMAC verification.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> WebhookAsync()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();
        var signature = Request.Headers["Paymongo-Signature"].FirstOrDefault();

        var result = await Sender.Send(new HandlePaymentWebhookCommand(payload, signature));
        return HandleResponse(result);
    }

    /// <summary>Staff reconciliation queue: online payments received but awaiting OR encoding.</summary>
    [HttpGet("awaiting-or")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<OnlinePaymentAwaitingOrDto>>> AwaitingOrAsync()
    {
        var result = await Sender.Send(new GetOnlinePaymentsAwaitingOrQuery());
        return HandleResponse(result);
    }

    /// <summary>Staff encode the manual OR number for a received online payment (admin/head on web, or
    /// a collector in the field). Uses the dedicated command so online attribution (no collector) is kept.</summary>
    [HttpPost("{id:guid}/or-number")]
    [Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<bool>> IssueOrNumberAsync(Guid id, [FromBody] IssueOnlinePaymentOrNumberRequest request)
    {
        var result = await Sender.Send(new IssueOnlinePaymentOrNumberCommand(id, request.ORNumber));
        return HandleResponse(result);
    }
}

/// <summary>Request body for OR encoding (the transaction id comes from the route).</summary>
public record IssueOnlinePaymentOrNumberRequest(string ORNumber);

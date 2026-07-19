using System.Text;
using EEMOCantilanSDS.Application.Command.OnlinePayments.Confirm;
using EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;
using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.OnlinePayments.GetAwaitingOr;
using EEMOCantilanSDS.Application.Queries.OnlinePayments.GetDashboard;
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

    /// <summary>
    /// Per-LGU PayMongo webhook. An LGU that runs its own PayMongo account points its webhook here (with
    /// its tenant code); authenticity is verified against THAT LGU's webhook secret. The default LGU
    /// (Cantilan) keeps using the tenant-less <c>/webhook</c> endpoint + the global secret, unchanged.
    /// </summary>
    [HttpPost("webhook/{tenantCode}")]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> WebhookForTenantAsync(string tenantCode)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();
        var signature = Request.Headers["Paymongo-Signature"].FirstOrDefault();

        var result = await Sender.Send(new HandlePaymentWebhookCommand(payload, signature, tenantCode));
        return HandleResponse(result);
    }

    /// <summary>
    /// Reconciliation fallback (payor return). After GCash redirects the payor back, the success page
    /// calls this to verify the payment directly with PayMongo and settle it idempotently when paid —
    /// the safety net for when a webhook never arrives. The payor may only confirm their own payment.
    /// </summary>
    [HttpPost("confirm")]
    [Authorize(Roles = "Payor")]
    public async Task<ActionResult<ConfirmOnlinePaymentResultDto>> ConfirmAsync([FromBody] ConfirmOnlinePaymentCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }

    /// <summary>
    /// Staff reconcile a specific online payment by reference — recovers a transaction that was paid at
    /// the gateway but is still Pending in our DB (e.g. the webhook never fired). Verifies with PayMongo
    /// and settles idempotently; never edits state blindly.
    /// </summary>
    [HttpPost("{reference}/reconcile")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<ConfirmOnlinePaymentResultDto>> ReconcileAsync(string reference)
    {
        var result = await Sender.Send(new ConfirmOnlinePaymentCommand(reference));
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

    /// <summary>Treasury overview + recent settled history for the admin Online Payments page (current LGU).</summary>
    [HttpGet("dashboard")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<OnlinePaymentDashboardDto>> DashboardAsync()
    {
        var result = await Sender.Send(new GetOnlinePaymentDashboardQuery());
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

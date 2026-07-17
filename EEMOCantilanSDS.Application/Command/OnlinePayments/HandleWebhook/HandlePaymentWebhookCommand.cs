using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;

/// <summary>
/// Processes a raw provider webhook. <see cref="Payload"/> is the verbatim request body and
/// <see cref="SignatureHeader"/> the provider signature header — both required to verify authenticity
/// before any state change. <see cref="TenantCode"/> is set only for the per-LGU webhook URL
/// (/api/onlinepayments/webhook/{tenantCode}); null means the default (Cantilan) webhook using the global secret.
/// </summary>
public record HandlePaymentWebhookCommand(string Payload, string? SignatureHeader, string? TenantCode = null) : IRequest<Result<bool>>;

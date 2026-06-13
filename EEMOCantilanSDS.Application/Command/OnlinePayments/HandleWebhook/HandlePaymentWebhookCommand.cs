using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;

/// <summary>
/// Processes a raw provider webhook. <see cref="Payload"/> is the verbatim request body and
/// <see cref="SignatureHeader"/> the provider signature header — both required to verify authenticity
/// before any state change.
/// </summary>
public record HandlePaymentWebhookCommand(string Payload, string? SignatureHeader) : IRequest<Result<bool>>;

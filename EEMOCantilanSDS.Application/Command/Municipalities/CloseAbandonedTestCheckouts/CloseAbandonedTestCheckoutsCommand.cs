using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.CloseAbandonedTestCheckouts;

/// <summary>Explicit Head-only cleanup: closes only provider-verified TEST checkouts.</summary>
public record CloseAbandonedTestCheckoutsCommand
    : IRequest<Result<CloseAbandonedTestCheckoutsResultDto>>;

public sealed record CloseAbandonedTestCheckoutsResultDto(int Expired, int Blocked, string Message);

public sealed class CloseAbandonedTestCheckoutsCommandHandler(
    IOnlinePaymentRepository transactions,
    ICurrentUserService user,
    IPayMongoCredentialResolver credentials,
    IPayMongoCheckoutExpiryService expiry)
    : IRequestHandler<CloseAbandonedTestCheckoutsCommand, Result<CloseAbandonedTestCheckoutsResultDto>>
{
    public async Task<Result<CloseAbandonedTestCheckoutsResultDto>> Handle(
        CloseAbandonedTestCheckoutsCommand request, CancellationToken ct)
    {
        if (user.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty
            || !string.Equals(user.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            return Result<CloseAbandonedTestCheckoutsResultDto>.Forbidden();

        var account = await credentials.ResolveAsync(ct);
        if (!account.IsConfigured
            || !account.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal))
            return Result<CloseAbandonedTestCheckoutsResultDto>.Failure(
                "This action is available only for the current office's saved PayMongo Test account.",
                ResultStatus.Invalid);

        // Captured payments requiring reconciliation are never part of abandoned-checkout cleanup.
        var reconciliation = await transactions.HasCapturedReconciliationAsync(municipalityId, ct);
        if (reconciliation)
            return Result<CloseAbandonedTestCheckoutsResultDto>.Failure(
                "This office has a captured payment requiring reconciliation. Resolve it before changing merchant accounts.",
                ResultStatus.Conflict);

        var outstanding = await transactions.GetUnsettledMerchantCheckoutsAsync(municipalityId, 50, ct);

        if (outstanding.Count > 50)
            return Result<CloseAbandonedTestCheckoutsResultDto>.Failure(
                "More than 50 unfinished checkouts exist. Ask treasury to review them individually.",
                ResultStatus.Conflict);

        var expired = 0;
        var blocked = 0;
        foreach (var transaction in outstanding)
        {
            var result = await expiry.ExpireAndVerifyAsync(transaction.GatewayReference!, ct);
            if (result.IsSuccess && result.Value is { Type: PaymentGatewayEventType.Expired })
            {
                // Minimal provider evidence only. The database condition checks the CURRENT
                // persisted status so an intervening paid webhook cannot be overwritten.
                var evidence = System.Text.Json.JsonSerializer.Serialize(new
                {
                    source = "PayMongo",
                    checkout = transaction.GatewayReference,
                    status = "expired",
                    verifiedAtUtc = DateTime.UtcNow,
                    actor = "Head abandoned-test-checkout review"
                });
                if (await transactions.TryExpireUnsettledCheckoutAsync(
                    municipalityId, transaction.Id, transaction.GatewayReference!, evidence, ct))
                    expired++;
                else
                    blocked++;
            }
            else
            {
                // Paid, in process, still active, unknown or provider unavailable: protected.
                blocked++;
            }
        }

        return Result<CloseAbandonedTestCheckoutsResultDto>.Success(
            new CloseAbandonedTestCheckoutsResultDto(expired, blocked,
                blocked == 0
                    ? $"{expired} Test checkout(s) confirmed expired by PayMongo. You can now save the replacement Live key."
                    : $"{expired} Test checkout(s) closed; {blocked} remain protected. Reconcile these before changing the merchant account."));
    }
}

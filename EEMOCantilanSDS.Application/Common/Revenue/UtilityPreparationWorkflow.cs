using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Optional office preparation of an approved ECF / WCF amount for a verified source and month - one source or several, through the SAME
/// existing writers (the ECF utility-bill writer and the WCF establish-obligation writer), so every freeze, eligibility and
/// settlement rule they enforce still applies. Preparation records an obligation only: no Collection, no SRC, no cash. Each source is
/// its own independent amount; a source that cannot be prepared (frozen by money, a direct-collection period, a stale version, no verified
/// service) is refused with its reason while the others still proceed, and a repeated request for an unchanged amount changes nothing.
/// </summary>
public sealed class UtilityPreparationWorkflow(ISender sender, WcfCollectionWorkflow wcf, IUtilityBillRepository bills, ICurrentUserService user)
{
    public const int MaxItems = 100;
    private const decimal MaxAmount = 1_000_000m;

    public async Task<Result<UtilityPrepareResult>> PrepareAsync(UtilityPrepareRequest request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin")) return Result<UtilityPrepareResult>.Forbidden();
        if (request.Year is < 2000 or > 2200 || request.Month is < 1 or > 12 || !Enum.IsDefined(request.Kind))
            return Result<UtilityPrepareResult>.Failure("Choose a valid billing month.", ResultStatus.Invalid);
        if (request.Items is null || request.Items.Count == 0 || request.Items.Count > MaxItems)
            return Result<UtilityPrepareResult>.Failure($"Prepare between 1 and {MaxItems} sources at a time.", ResultStatus.Invalid);
        if (request.Items.Select(x => x.StallId).Distinct().Count() != request.Items.Count)
            return Result<UtilityPrepareResult>.Failure("A source can appear only once in a batch.", ResultStatus.Invalid);

        var outcomes = new List<UtilityPrepareOutcome>();
        foreach (var item in request.Items)
        {
            if (item.ApprovedAmount <= 0m || item.ApprovedAmount > MaxAmount || decimal.Round(item.ApprovedAmount, 2, MidpointRounding.ToZero) != item.ApprovedAmount)
            {
                outcomes.Add(new(item.StallId, false, "Enter a positive approved amount with at most two decimal places."));
                continue;
            }
            try { outcomes.Add(await PrepareOneAsync(request, item, ct)); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                outcomes.Add(new(item.StallId, false, ex.Message));
            }
        }
        return Result<UtilityPrepareResult>.Success(new(outcomes));
    }

    private async Task<UtilityPrepareOutcome> PrepareOneAsync(UtilityPrepareRequest request, UtilityPrepareItem item, CancellationToken ct)
    {
        var bill = await bills.GetByStallAndMonthAsync(item.StallId, request.Year, request.Month, ct);
        var current = bill is null ? 0L : request.Kind == UtilityKind.Electricity ? bill.ElectricitySourceVersion : bill.WaterSourceVersion;
        if (item.ExpectedVersion is { } expected && expected != current)
            return new(item.StallId, false, "This source changed after it was shown. Refresh and review it again.");
        if (bill is not null && (request.Kind == UtilityKind.Electricity ? bill.ElectricityDirectCollection : bill.WaterDirectCollection))
            return new(item.StallId, false, "This period is collected directly; an amount cannot be prepared after the first direct collection.");

        if (request.Kind == UtilityKind.Water)
        {
            var result = await wcf.EstablishObligationAsync(new(item.StallId, request.Year, request.Month, item.ApprovedAmount), ct);
            return new(item.StallId, result.IsSuccess, result.IsSuccess ? null : result.Error ?? "The amount could not be prepared.");
        }

        // The ECF writer saves both parts of a bill, so the Water part is sent back exactly as it stands (never reset to zero).
        var command = new RecordUtilityReadingCommand(item.StallId, request.Year, request.Month, 0m, 0m, 0m,
            bill?.WaterPreviousReading ?? 0m, bill?.WaterCurrentReading ?? 0m, bill?.WaterRatePerCubicMeter ?? 0m, bill?.Remarks,
            ElecApprovedAmount: item.ApprovedAmount);
        var saved = await sender.Send(command, ct);
        return new(item.StallId, saved.IsSuccess, saved.IsSuccess ? null : saved.Error ?? "The amount could not be prepared.");
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class FeeScheduleCollectionWorkflow
{
    public async Task<Result<TaboBatchQuoteDto>> QuoteTaboBatchAsync(TaboBatchRequest request, CancellationToken ct = default)
    {
        if (!ValidTaboBatch(request)) return Result<TaboBatchQuoteDto>.Failure("Choose 1-200 distinct registered vendors with stable operation identities.", ResultStatus.Invalid);
        var tenant = municipality.MunicipalityId;
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collector ||
            tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant ||
            !await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.Id == collector && x.MunicipalityId == tenant && x.IsActive &&
                x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.TPM), ct)) return Result<TaboBatchQuoteDto>.Forbidden();
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.OperationCode == CollectorOperationCodes.Tabo, ct);
        var versions = service is null ? [] : await db.GovernedServiceSettings.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.GovernedServiceId == service.Id).ToListAsync(ct);
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.IsActive && x.SemanticCode == RevenueClassificationCodes.Tabo, ct);
        var policies = classification is null ? [] : await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.RevenueClassificationId == classification.Id && x.BusinessContext == RevenuePolicyContext.Default).ToListAsync(ct);
        var rows = new List<TaboBatchItemQuote>();
        foreach (var item in request.Items)
        {
            var setting = GovernedServiceSetting.Resolve(versions, item.BusinessDate);
            var policy = policies.Where(x => x.EffectiveDate <= item.BusinessDate).OrderByDescending(x => x.EffectiveDate).FirstOrDefault();
            if (item.BusinessDate == default || item.BusinessDate > BusinessToday || setting is not { IsEnabled: true, MobileEnabled: true } || policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            {
                rows.Add(new(item.ClientOperationId, item.VendorId, item.BusinessDate, 0m, null, "SourceNotAvailable", "Review the market day and approved collection policy.", null));
                continue;
            }
            // Registered IDs only: ResolveTaboAsync is read-only on this path and owns vendor/day/rate/duplicate rules.
            var resolved = await ResolveTaboAsync(tenant, item, ct);
            var problem = resolved.Problem;
            if (problem is null && (item.ReceivedAmount != resolved.Amount ||
                setting.MaximumAmount is { } maximum && resolved.Amount > maximum))
                rows.Add(new(item.ClientOperationId, item.VendorId, item.BusinessDate, 0m, null, "AmountChanged", "Review the approved Tabo amount.", null));
            else rows.Add(new(item.ClientOperationId, item.VendorId, item.BusinessDate, resolved.Amount,
                policy.PermittedInstrumentType, problem?.Code, problem?.Message,
                JsonSerializer.Serialize(new { setting.Id, Policy = policy.Id, resolved.Facts, resolved.Amount }, JsonOptions)));
        }
        var ordered = rows.OrderBy(x => x.ClientOperationId).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ordered, JsonOptions))));
        return Result<TaboBatchQuoteDto>.Success(new(rows, rows.Sum(x => x.Amount), rows.All(x => x.ProblemCode is null), fingerprint));
    }

    public async Task<Result<TaboBatchOutcomeDto>> RecordTaboBatchAsync(TaboBatchRequest request, CancellationToken ct = default)
    {
        if (!ValidTaboBatch(request)) return Result<TaboBatchOutcomeDto>.Failure("Choose valid registered vendor/day intents.", ResultStatus.Invalid);
        await using var transaction = await db.BeginSerializableTransactionAsync(ct);
        var ids = request.Items.Select(x => x.ClientOperationId).ToArray();
        var priorCount = await db.PostingOperations.AsNoTracking().CountAsync(x => x.MunicipalityId == municipality.MunicipalityId && ids.Contains(x.ClientOperationId) && x.Status == PostingOperationStatus.Succeeded, ct);
        if (priorCount != ids.Length)
        {
            var quote = await QuoteTaboBatchAsync(request, ct);
            if (!quote.IsSuccess) return Result<TaboBatchOutcomeDto>.Failure(quote.Error!, quote.Status);
            if (!quote.Value!.CanRecord || quote.Value.Fingerprint != request.QuoteFingerprint)
                return Result<TaboBatchOutcomeDto>.Failure("QuoteStale", ResultStatus.Conflict);
        }
        var results = new List<GovernedServiceOutcomeDto>();
        foreach (var item in request.Items)
        {
            var posted = await PostMobileAsync(item, ct);
            if (!posted.IsSuccess)
            {
                db.ChangeTracker.Clear();
                return Result<TaboBatchOutcomeDto>.Failure(posted.Error!, posted.Status);
            }
            results.Add(posted.Value!);
        }
        await transaction.CommitAsync(ct);
        return Result<TaboBatchOutcomeDto>.Success(new(results, results.Sum(x => x.Amount)));
    }

    private static bool ValidTaboBatch(TaboBatchRequest request) => request.Items is { Count: > 0 and <= 200 } &&
        request.Items.All(x => x.OperationCode == CollectorOperationCodes.Tabo && x.ClientOperationId != Guid.Empty && x.VendorId is { } id && id != Guid.Empty) &&
        request.Items.Select(x => x.ClientOperationId).Distinct().Count() == request.Items.Count &&
        request.Items.Select(x => (x.VendorId, x.BusinessDate)).Distinct().Count() == request.Items.Count;
}

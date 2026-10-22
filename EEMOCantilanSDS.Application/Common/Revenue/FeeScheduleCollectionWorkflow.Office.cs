using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Office (Head/Admin) recording of a Slaughterhouse transaction by approved charges. It is the same calculation, snapshot and
/// posting as the Collector path, so the total is the server's and an itemized receipt reads identically wherever it was taken; only
/// the actor differs (no collector attribution, its own idempotency origin). It exists only while the Head has Slaughterhouse on
/// approved charges: before that the existing office writer is unchanged.
/// </summary>
public sealed partial class FeeScheduleCollectionWorkflow
{
    private const string OfficeOrigin = "OfficeFeeSchedule";

    private bool IsOffice(out Guid tenant)
    {
        tenant = municipality.MunicipalityId;
        var claimed = currentUser.MunicipalityId;
        return currentUser.IsAuthenticated && (currentUser.Role == "SuperAdmin" || currentUser.Role == "Admin")
            && currentUser.UserId is { } id && id != Guid.Empty && tenant != Guid.Empty && (claimed is null || claimed == tenant);
    }

    public Task<Result<SlaughterTermsDto>> GetOfficeSlaughterTermsAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? BusinessToday;
        return !IsOffice(out var tenant) || day > BusinessToday
            ? Task.FromResult(Result<SlaughterTermsDto>.Forbidden())
            : BuildTermsAsync(tenant, day, false, ct);
    }

    public Task<Result<SlaughterQuote>> QuoteOfficeSlaughterAsync(FeeSchedulePostRequest request, CancellationToken ct = default) =>
        !IsOffice(out var tenant) || request.OperationCode != CollectorOperationCodes.Slaughterhouse
            || request.BusinessDate == default || request.BusinessDate > BusinessToday
            ? Task.FromResult(Result<SlaughterQuote>.Forbidden())
            : QuoteSlaughterCoreAsync(request, tenant, null, currentUser.Username ?? "Office", false, ct);

    public Task<Result<GovernedServiceOutcomeDto>> PostOfficeSlaughterAsync(FeeSchedulePostRequest request, CancellationToken ct = default)
    {
        if (!IsOffice(out var tenant) || request.OperationCode != CollectorOperationCodes.Slaughterhouse)
            return Task.FromResult(Result<GovernedServiceOutcomeDto>.Forbidden());
        if (request.ClientOperationId == Guid.Empty)
            return Task.FromResult(Result<GovernedServiceOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid));
        return PostCoreAsync(request, new Poster(tenant, null, currentUser.Username ?? "Office", currentUser.UserId!.Value.ToString("N"),
            currentUser.Role!, OfficeOrigin, false), ct);
    }

    /// <summary>True when the Head has Slaughterhouse on approved charges for the date: the typed-OR per-head office writer is then closed.</summary>
    public async Task<bool> IsSlaughterItemizedAsync(DateOnly date, CancellationToken ct = default)
    {
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty) return false;
        var service = await db.GovernedServices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.OperationCode == CollectorOperationCodes.Slaughterhouse, ct);
        if (service is null) return false;
        var setting = GovernedServiceSetting.Resolve(await db.GovernedServiceSettings.AsNoTracking()
            .Where(x => x.MunicipalityId == tenant && x.GovernedServiceId == service.Id).ToListAsync(ct), date);
        return setting is { IsEnabled: true, Basis: GovernedServiceBasis.ApprovedFeeOption };
    }
}

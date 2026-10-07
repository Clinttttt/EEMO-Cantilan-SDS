using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;
public sealed partial class OfficeCollectionWorkflow
{
    public async Task<Result<VendorRegistryImportPreview>> PreviewImportAsync(VendorRegistryImportRequest request, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role != "SuperAdmin") return Result<VendorRegistryImportPreview>.Forbidden();
        if (request.Rows is null || request.Rows.Count is < 1 or > 500 || request.Rows.Any(x => x is null || x.Registration is null)
            || request.Rows.Select(x => x.RowNumber).Distinct().Count() != request.Rows.Count)
            return Result<VendorRegistryImportPreview>.Failure("InvalidImport", ResultStatus.Invalid);
        var years = request.Rows.Select(x => x.Registration.TaxYear).Distinct().ToArray();
        var operationIds = request.Rows.Select(x => x.Registration.ClientOperationId).ToArray();
        var existing = await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && (years.Contains(x.TaxYear) || operationIds.Contains(x.ClientOperationId))).ToListAsync(ct);
        var result = new List<VendorRegistryImportPreviewRow>();
        foreach (var row in request.Rows)
        {
            var r = row.Registration;
            var problems = new List<VendorRegistryImportProblem>();
            FishMeatVendorRegistration? candidate = null;
            try { candidate = FishMeatVendorRegistration.Register(Tenant, r.ClientOperationId, r.TaxYear, r.VendorType, r.RegistrationKind,
                r.DisplayName, r.BusinessName, r.Address, r.Reference, user.Username ?? "Head"); }
            catch (ArgumentException) { problems.Add(new("InvalidRegistration", "Review the name, year, vendor type and registration details.")); }
            if (row.RowNumber < 1 || request.Rows.Count(x => x.Registration.ClientOperationId == r.ClientOperationId) != 1)
                problems.Add(new("DuplicateRowIdentity", "Each row needs a unique operation identity."));
            var prior = existing.SingleOrDefault(x => x.ClientOperationId == r.ClientOperationId);
            if (prior is not null && candidate is not null && ToDto(prior) != ToDto(candidate) with { Id = prior.Id })
                problems.Add(new("RegistrationIntentConflict", "This operation already saved different registration details."));
            var duplicateIds = existing.Where(x => x.ClientOperationId != r.ClientOperationId && x.TaxYear == r.TaxYear
                && x.DisplayName.Equals(candidate?.DisplayName, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToArray();
            var duplicateRow = request.Rows.Any(x => x.RowNumber != row.RowNumber && x.Registration.TaxYear == r.TaxYear
                && string.Equals(x.Registration.DisplayName?.Trim(), candidate?.DisplayName, StringComparison.OrdinalIgnoreCase));
            var needsReview = prior is null && (duplicateIds.Length > 0 || duplicateRow) && !row.ConfirmSeparateRegistration;
            if (needsReview) problems.Add(new("PossibleDuplicate", "Confirm this is a separate registration. Names are never merged."));
            var normalized = candidate is null ? r : r with { DisplayName = candidate.DisplayName, BusinessName = candidate.BusinessName,
                Address = candidate.Address, Reference = candidate.Reference };
            result.Add(new(row.RowNumber, normalized, problems.Any(x => x.Code != "PossibleDuplicate") ? VendorRegistryImportState.Invalid
                : needsReview ? VendorRegistryImportState.PossibleDuplicateRequiresReview : VendorRegistryImportState.New, problems, duplicateIds));
        }
        return Result<VendorRegistryImportPreview>.Success(new(result));
    }
    public async Task<Result<VendorRegistryImportResult>> SaveImportAsync(VendorRegistryImportRequest request, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role != "SuperAdmin") return Result<VendorRegistryImportResult>.Forbidden();
        var committed = false;
        try
        {
            // One save, same domain factory and replay identity as single registration; never recover a row inside an aborted transaction.
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var preview = await PreviewImportAsync(request, ct);
            if (!preview.IsSuccess) return Result<VendorRegistryImportResult>.Failure(preview.Error ?? "InvalidImport", preview.Status);
            if (!preview.Value!.CanSave) return Result<VendorRegistryImportResult>.Failure("ImportNeedsReview", ResultStatus.Invalid);
            var ids = preview.Value.Rows.Select(x => x.Registration.ClientOperationId).ToArray();
            var existing = await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.ClientOperationId))
                .ToDictionaryAsync(x => x.ClientOperationId, ct);
            var rows = new List<VendorRegistryImportSavedRow>();
            foreach (var row in preview.Value.Rows)
            {
                var r = row.Registration;
                if (!existing.TryGetValue(r.ClientOperationId, out var registration))
                {
                    registration = FishMeatVendorRegistration.Register(Tenant, r.ClientOperationId, r.TaxYear, r.VendorType,
                        r.RegistrationKind, r.DisplayName, r.BusinessName, r.Address, r.Reference, user.Username ?? "Head");
                    db.FishMeatVendorRegistrations.Add(registration);
                }
                rows.Add(new(row.RowNumber, ToDto(registration)));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); committed = true;
            return Result<VendorRegistryImportResult>.Success(new(rows));
        }
        catch (DbUpdateException) { return Result<VendorRegistryImportResult>.Failure("ConcurrentImport", ResultStatus.Conflict); }
        catch (InvalidOperationException e) when (e.InnerException is DbUpdateException)
        { return Result<VendorRegistryImportResult>.Failure("ConcurrentImport", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }
    public async Task<Result<VendorRegistrySummary>> RegistrySummaryAsync(int year, CancellationToken ct = default)
    {
        var registrations = await RegistrationsAsync(year, ct);
        if (!registrations.IsSuccess) return Result<VendorRegistrySummary>.Failure(registrations.Error ?? "InvalidPeriod", registrations.Status);
        var today = clock.PhilippineToday;
        var activity = await ActivityAsync(new(today.Year, today.Month, 1), today, ct: ct);
        var rows = registrations.Value!;
        return Result<VendorRegistrySummary>.Success(new(year, today.Year, new(today.Year, today.Month, 1), today, rows.Count, rows.Count(x => x.VendorType == FishMeatVendorType.Fish),
            rows.Count(x => x.VendorType == FishMeatVendorType.Meat), rows,
            (activity.Value ?? []).Where(x => x.OperationCode is CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure).ToArray()));
    }
}

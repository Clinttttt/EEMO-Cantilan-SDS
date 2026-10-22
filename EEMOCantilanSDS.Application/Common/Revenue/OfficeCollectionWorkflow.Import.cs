using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;
public sealed partial class OfficeCollectionWorkflow
{
    private static string NormalizeImportName(string? name) =>
        System.Text.RegularExpressions.Regex.Replace((name ?? string.Empty).Trim(), @"\s+", " ");
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
        // Cross-registry candidates are advisory only. Never infer a verified link from a name.
        // One bounded office-candidate read per tax year, rather than one query per uploaded row.
        var npmByYear = new Dictionary<int, IReadOnlyList<NpmVendorCandidate>>();
        foreach (var year in years)
        {
            var candidates = await NpmVendorCandidatesAsync(year, historical: false, exceptions: true, ct: ct);
            if (!candidates.IsSuccess) return Result<VendorRegistryImportPreview>.Failure(candidates.Error ?? "NpmCandidatesUnavailable", candidates.Status);
            npmByYear[year] = candidates.Value!.Rows;
        }
        var result = new List<VendorRegistryImportPreviewRow>();
        foreach (var row in request.Rows)
        {
            var r = row.Registration;
            var problems = new List<VendorRegistryImportProblem>();
            FishMeatVendorRegistration? candidate = null;
            try { candidate = FishMeatVendorRegistration.Register(Tenant, r.ClientOperationId, r.TaxYear, r.VendorType, r.RegistrationKind,
                r.DisplayName, r.BusinessName, r.Address, r.Reference, user.Username ?? "Head",
                r.LastName, r.FirstName, r.MiddleName, r.NameExtension, r.Sex, r.NatureOfBusiness); }
            catch (ArgumentException) { problems.Add(new("InvalidRegistration", "Review the name, year, vendor type and registration details.")); }
            if (row.RowNumber < 1 || request.Rows.Count(x => x.Registration.ClientOperationId == r.ClientOperationId) != 1)
                problems.Add(new("DuplicateRowIdentity", "Each row needs a unique operation identity."));
            var prior = existing.SingleOrDefault(x => x.ClientOperationId == r.ClientOperationId);
            if (prior is not null && candidate is not null && ToDto(prior) != ToDto(candidate) with { Id = prior.Id })
                problems.Add(new("RegistrationIntentConflict", "This operation already saved different registration details."));
            var duplicateIds = existing.Where(x => x.ClientOperationId != r.ClientOperationId && x.TaxYear == r.TaxYear
                && string.Equals(NormalizeImportName(x.DisplayName), NormalizeImportName(candidate?.DisplayName), StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id).ToArray();
            var duplicateRow = request.Rows.Any(x => x.RowNumber != row.RowNumber && x.Registration.TaxYear == r.TaxYear
                && string.Equals(NormalizeImportName(x.Registration.DisplayName), NormalizeImportName(candidate?.DisplayName), StringComparison.OrdinalIgnoreCase));
            var reuse = row.ReuseRegistrationId.HasValue
                ? existing.SingleOrDefault(x => x.Id == row.ReuseRegistrationId.Value) : null;
            if (row.ReuseRegistrationId.HasValue && (reuse is null || reuse.TaxYear != r.TaxYear
                || reuse.VendorType != r.VendorType || reuse.Status != VendorRegistrationStatus.Active
                || !string.Equals(NormalizeImportName(reuse.DisplayName), NormalizeImportName(candidate?.DisplayName), StringComparison.OrdinalIgnoreCase)
                || row.ConfirmSeparateRegistration || prior is not null))
                problems.Add(new("InvalidExistingRegistration", "Select a current, matching annual registration to reuse."));
            if (row.ReuseRegistrationId.HasValue && request.Rows.Count(x => x.ReuseRegistrationId == row.ReuseRegistrationId) > 1)
                problems.Add(new("DuplicateExistingSelection", "An existing registration cannot be reused twice in the same list."));
            var needsReview = prior is null && reuse is null && (duplicateIds.Length > 0 || duplicateRow) && !row.ConfirmSeparateRegistration;
            if (needsReview) problems.Add(new("PossibleDuplicate", "Confirm this is a separate registration. Names are never merged."));
            var normalized = candidate is null ? r : r with { DisplayName = candidate.DisplayName, BusinessName = candidate.BusinessName,
                Address = candidate.Address, Reference = candidate.Reference, LastName = candidate.LastName,
                FirstName = candidate.FirstName, MiddleName = candidate.MiddleName, NameExtension = candidate.NameExtension,
                Sex = candidate.Sex, NatureOfBusiness = candidate.NatureOfBusiness };
            var npmMatches = npmByYear[r.TaxYear].Where(x => candidate is not null
                && string.Equals(NormalizeImportName(x.Occupant), NormalizeImportName(candidate.DisplayName), StringComparison.OrdinalIgnoreCase))
                .Select(x => new VendorRegistryImportNpmMatch(x.ContractId, x.StallId, x.Occupant, x.StallNo, x.Area,
                    x.Links.Any(link => link.CorrectedAtUtc is null))).ToArray();
            var choices = existing.Where(x => x.TaxYear == r.TaxYear
                && string.Equals(NormalizeImportName(x.DisplayName), NormalizeImportName(candidate?.DisplayName), StringComparison.OrdinalIgnoreCase))
                .Select(x => new VendorRegistryImportExistingMatch(x.Id, x.DisplayName, x.BusinessName, x.VendorType, x.Status)).ToArray();
            result.Add(new(row.RowNumber, normalized, problems.Any(x => x.Code != "PossibleDuplicate") ? VendorRegistryImportState.Invalid
                : reuse is not null ? VendorRegistryImportState.UseExisting
                : needsReview ? VendorRegistryImportState.PossibleDuplicateRequiresReview : VendorRegistryImportState.New,
                problems, duplicateIds, npmMatches, choices));
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
            var reuseIds = request.Rows.Where(x => x.ReuseRegistrationId.HasValue).Select(x => x.ReuseRegistrationId!.Value).Distinct().ToArray();
            var reusable = await db.FishMeatVendorRegistrations.AsNoTracking()
                .Where(x => x.MunicipalityId == Tenant && reuseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var rows = new List<VendorRegistryImportSavedRow>();
            foreach (var row in preview.Value.Rows)
            {
                var r = row.Registration;
                var selected = request.Rows.Single(x => x.RowNumber == row.RowNumber).ReuseRegistrationId;
                if (selected.HasValue)
                {
                    // The preview was recomputed inside this serializable transaction. Never create
                    // a new registration when Head chose an existing exact annual identity.
                    rows.Add(new(row.RowNumber, ToDto(reusable[selected.Value]), Reused: true));
                    continue;
                }
                if (!existing.TryGetValue(r.ClientOperationId, out var registration))
                {
                    registration = FishMeatVendorRegistration.Register(Tenant, r.ClientOperationId, r.TaxYear, r.VendorType,
                        r.RegistrationKind, r.DisplayName, r.BusinessName, r.Address, r.Reference, user.Username ?? "Head",
                        r.LastName, r.FirstName, r.MiddleName, r.NameExtension, r.Sex, r.NatureOfBusiness);
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

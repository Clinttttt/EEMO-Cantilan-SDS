using System.Data.Common;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;
public sealed partial class OfficeCollectionWorkflow
{
    private async Task<bool> CanRequestNpm(CancellationToken ct) =>
        await IsAssignedAsync(CollectorOperationCodes.FishMeatVendorFee, ct) || await IsAssignedAsync(CollectorOperationCodes.WeightAndMeasure, ct);
    private static NpmRegistrationRequestDto RequestDto(NpmRegistrationRequest x) => new(x.Id, x.VendorRegistrationId,
        x.TaxYear, x.VendorName, x.CollectorId, x.CollectorName, x.ProposedStall, x.ProposedSection, x.Note,
        x.SubmittedAtUtc, x.Status, x.Version, x.ReviewedBy, x.ReviewedAtUtc, x.ReviewReason, x.AssociationId, x.ReplacesRequestId);

    public async Task<Result<NpmRegistrationRequestPage>> NpmRequestsAsync(int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var headRead = TenantValid && user.IsAuthenticated && user.Role is "SuperAdmin" or "Admin";
        if (!headRead && !await CanRequestNpm(ct)) return Result<NpmRegistrationRequestPage>.Forbidden();
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100) return Result<NpmRegistrationRequestPage>.Failure("Invalid pagination.", ResultStatus.Invalid);
        var query = db.NpmRegistrationRequests.AsNoTracking().Where(x => x.MunicipalityId == Tenant);
        if (!headRead) query = query.Where(x => x.CollectorId == user.CollectorId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Status != NpmRegistrationRequestStatus.Submitted)
            .ThenByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result<NpmRegistrationRequestPage>.Success(new(rows.Select(RequestDto).ToArray(), total, page, pageSize));
    }

    public async Task<Result<NpmRegistrationRequestDto>> SubmitNpmRequestAsync(SubmitNpmRegistrationRequest request, CancellationToken ct = default)
    {
        if (!await CanRequestNpm(ct) || user.CollectorId is not { } collector) return Result<NpmRegistrationRequestDto>.Forbidden();
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var fingerprint = AssociationHash(new { collector, request });
            var replay = await db.NpmRegistrationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
            if (replay is not null) return replay.CollectorId == collector && replay.Fingerprint == fingerprint
                ? Result<NpmRegistrationRequestDto>.Success(RequestDto(replay)) : Result<NpmRegistrationRequestDto>.Failure("Request intent conflict.", ResultStatus.Conflict);
            var vendor = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == request.VendorRegistrationId, ct);
            if (vendor is null) return Result<NpmRegistrationRequestDto>.NotFound();
            if (vendor.Status != VendorRegistrationStatus.Active || vendor.TaxYear != clock.PhilippineToday.Year)
                return Result<NpmRegistrationRequestDto>.Failure("Select an active registration for the current tax year.", ResultStatus.Invalid);
            if (request.ReplacesRequestId is { } prior && !await db.NpmRegistrationRequests.AnyAsync(x => x.MunicipalityId == Tenant && x.Id == prior
                && x.CollectorId == collector && x.VendorRegistrationId == vendor.Id && x.Status == NpmRegistrationRequestStatus.Returned, ct))
                return Result<NpmRegistrationRequestDto>.Failure("Invalid returned request reference.", ResultStatus.Invalid);
            if (await db.NpmRegistrationRequests.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectorId == collector
                && x.VendorRegistrationId == vendor.Id && x.Status == NpmRegistrationRequestStatus.Submitted, ct))
                return Result<NpmRegistrationRequestDto>.Failure("A request for this vendor is already pending. Refresh your requests.", ResultStatus.Conflict);
            var row = NpmRegistrationRequest.Submit(Tenant, collector, user.Username ?? "Collector", vendor.Id, vendor.TaxYear,
                vendor.DisplayName, request.ClientOperationId, fingerprint, request.ProposedStall, request.ProposedSection, request.Note, clock.UtcNow, request.ReplacesRequestId);
            db.NpmRegistrationRequests.Add(row); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<NpmRegistrationRequestDto>.Success(RequestDto(row));
        }
        catch (ArgumentException e) { return Result<NpmRegistrationRequestDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { return Result<NpmRegistrationRequestDto>.Failure("Request concurrency conflict; refresh before retrying.", ResultStatus.Conflict); }
        catch (DbException) { return Result<NpmRegistrationRequestDto>.Failure("Request concurrency conflict; refresh before retrying.", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    public async Task<Result<NpmRegistrationRequestDto>> ReviewNpmRequestAsync(Guid id, ReviewNpmRegistrationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<NpmRegistrationRequestDto>.Forbidden();
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var row = await db.NpmRegistrationRequests.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);
            if (row is null) return Result<NpmRegistrationRequestDto>.NotFound();
            if (row.DecisionOperationId == request.ClientOperationId)
                return row.Status == request.Outcome && row.AssociationId == request.AssociationId && row.ReviewReason == (request.Reason ?? "").Trim()
                    ? Result<NpmRegistrationRequestDto>.Success(RequestDto(row)) : Result<NpmRegistrationRequestDto>.Failure("Review intent conflict.", ResultStatus.Conflict);
            if (request.Outcome == NpmRegistrationRequestStatus.Approved)
            {
                var link = await db.FishMeatNpmAssociations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
                    && x.Id == request.AssociationId && x.VendorRegistrationId == row.VendorRegistrationId && x.CorrectedAtUtc == null, ct);
                if (link is null) return Result<NpmRegistrationRequestDto>.Failure("Complete and verify the NPM registration and association first.", ResultStatus.Conflict);
                var vendor = await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync(x => x.Id == row.VendorRegistrationId && x.MunicipalityId == Tenant, ct);
                var stall = await db.Stalls.AsNoTracking().Include(x => x.Contracts).Include(x => x.Facility)
                    .SingleOrDefaultAsync(x => x.Id == link.StallId && x.MunicipalityId == Tenant && x.Facility!.Code == FacilityCode.NPM, ct);
                var today = clock.PhilippineToday;
                var occupancy = stall?.OccupancyAnsweringForMonth(today.Year, today.Month, today)?.Contract;
                if (vendor.Status != VendorRegistrationStatus.Active || vendor.TaxYear != today.Year || stall?.Status != StallStatus.Active
                    || occupancy?.Id != link.ContractId || occupancy.EffectivityDate > today)
                    return Result<NpmRegistrationRequestDto>.Failure("The linked registration is no longer applicable. Refresh and review the tenancy.", ResultStatus.Conflict);
            }
            row.Review(request.ClientOperationId, request.ExpectedVersion, request.Outcome, user.Username ?? "Head", request.Reason, clock.UtcNow, request.AssociationId);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<NpmRegistrationRequestDto>.Success(RequestDto(row));
        }
        catch (ArgumentException e) { return Result<NpmRegistrationRequestDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (InvalidOperationException e) { return Result<NpmRegistrationRequestDto>.Failure(e.Message, ResultStatus.Conflict); }
        catch (DbUpdateException) { return Result<NpmRegistrationRequestDto>.Failure("Request changed; refresh before reviewing.", ResultStatus.Conflict); }
        catch (DbException) { return Result<NpmRegistrationRequestDto>.Failure("Request concurrency conflict; refresh before reviewing.", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }
}

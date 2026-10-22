using System.Data.Common;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class OfficeCollectionWorkflow
{
    private bool VendorRequestsInstalled => registrationOptions?.RequestsAvailable(Tenant) == true
        && db.ChangeTracker.Context.Model.FindEntityType(typeof(FishMeatRegistrationRequest)) is not null;
    private async Task<bool> CanRequestVendor(CancellationToken ct) => await IsAssignedAsync(CollectorOperationCodes.FishMeatVendorFee, ct)
        || await IsAssignedAsync(CollectorOperationCodes.WeightAndMeasure, ct);
    private static FishMeatRegistrationRequestDto VendorRequestDto(FishMeatRegistrationRequest x) => new(x.Id,
        x.CollectorId, x.SubmittedBy, x.VendorName, x.VendorType, x.TaxYear, x.Contact, x.Note, x.SubmittedAtUtc,
        x.Status, x.Version, x.RegistrationId, x.ReviewedBy, x.ReviewedAtUtc, x.Reason, x.ReplacesRequestId, x.ReviewedByUserId);

    public async Task<Result<FishMeatRegistrationRequestPage>> VendorRequestsAsync(int page = 1, int pageSize = 25,
        FishMeatRequestStatus? status = null, CancellationToken ct = default)
    {
        var office = TenantValid && user.IsAuthenticated && user.Role is "SuperAdmin" or "Admin";
        if (!office && !await CanRequestVendor(ct)) return Result<FishMeatRegistrationRequestPage>.Forbidden();
        if (!VendorRequestsInstalled) return Result<FishMeatRegistrationRequestPage>.Failure("VendorRequestSchemaNotEnabled", ResultStatus.Failed);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || status.HasValue && !Enum.IsDefined(status.Value))
            return Result<FishMeatRegistrationRequestPage>.Failure("Invalid pagination or status.", ResultStatus.Invalid);
        var q = db.FishMeatRegistrationRequests.AsNoTracking().Where(x => x.MunicipalityId == Tenant);
        if (!office) q = q.Where(x => x.CollectorId == user.CollectorId);
        if (status.HasValue) q = q.Where(x => x.Status == status);
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.Status != FishMeatRequestStatus.Submitted).ThenByDescending(x => x.SubmittedAtUtc)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result<FishMeatRegistrationRequestPage>.Success(new(rows.Select(VendorRequestDto).ToArray(), count, page, pageSize));
    }

    public async Task<Result<FishMeatRegistrationRequestDto>> VendorRequestAsync(Guid id, CancellationToken ct = default)
    {
        var office = TenantValid && user.IsAuthenticated && user.Role is "SuperAdmin" or "Admin";
        if (!office && !await CanRequestVendor(ct)) return Result<FishMeatRegistrationRequestDto>.Forbidden();
        if (!VendorRequestsInstalled) return VendorRequestUnavailable();
        var row = await db.FishMeatRegistrationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
            && x.Id == id && (office || x.CollectorId == user.CollectorId), ct);
        return row is null ? Result<FishMeatRegistrationRequestDto>.NotFound() : Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(row));
    }

    private static Result<FishMeatRegistrationRequestDto> VendorRequestUnavailable() =>
        Result<FishMeatRegistrationRequestDto>.Failure("VendorRequestSchemaNotEnabled", ResultStatus.Failed);

    public async Task<Result<FishMeatRegistrationRequestDto>> SubmitVendorRequestAsync(SubmitFishMeatRegistrationRequest request, CancellationToken ct = default)
    {
        if (!await CanRequestVendor(ct) || user.CollectorId is not { } collector) return Result<FishMeatRegistrationRequestDto>.Forbidden();
        if (!VendorRequestsInstalled) return VendorRequestUnavailable();
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var fingerprint = AssociationHash(new { collector, request });
            var prior = await db.FishMeatRegistrationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
                && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null) return prior.CollectorId == collector && prior.Fingerprint == fingerprint
                ? Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(prior))
                : Result<FishMeatRegistrationRequestDto>.Failure("RequestIntentConflict", ResultStatus.Conflict);
            if (request.TaxYear != clock.PhilippineToday.Year)
                return Result<FishMeatRegistrationRequestDto>.Failure("Use the current tax year.", ResultStatus.Invalid);
            if (request.ReplacesRequestId is { } previous && !await db.FishMeatRegistrationRequests.AnyAsync(x =>
                x.MunicipalityId == Tenant && x.Id == previous && x.CollectorId == collector && x.Status == FishMeatRequestStatus.Returned, ct))
                return Result<FishMeatRegistrationRequestDto>.Failure("Invalid returned request reference.", ResultStatus.Invalid);
            var pendingKey = AssociationHash(new { Name = request.VendorName?.Trim().ToUpperInvariant(), request.VendorType,
                request.TaxYear, Contact = string.IsNullOrWhiteSpace(request.Contact) ? null : request.Contact.Trim(),
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() });
            var row = FishMeatRegistrationRequest.Submit(Tenant, collector, user.Username ?? "Collector", request.ClientOperationId,
                fingerprint, pendingKey, request.VendorName!, request.VendorType, request.TaxYear, request.Contact, request.Note,
                clock.UtcNow, request.ReplacesRequestId);
            if (await db.FishMeatRegistrationRequests.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectorId == collector
                && x.PendingKey == pendingKey && x.Status == FishMeatRequestStatus.Submitted, ct))
                return Result<FishMeatRegistrationRequestDto>.Failure("A similar request is pending. Review it; this is not a vendor identity match.", ResultStatus.Conflict);
            db.FishMeatRegistrationRequests.Add(row);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(row));
        }
        catch (ArgumentException e) { return Result<FishMeatRegistrationRequestDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (InvalidOperationException e) when (VendorRequestSerializationConflict(e)) { return VendorRequestConflict(); }
        catch (DbUpdateException) { return VendorRequestConflict(); }
        catch (DbException) { return VendorRequestConflict(); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    private static Result<FishMeatRegistrationRequestDto> VendorRequestConflict() =>
        Result<FishMeatRegistrationRequestDto>.Failure("Request conflict; refresh, then retry the same operation ID.", ResultStatus.Conflict);

    private static bool VendorRequestSerializationConflict(Exception error) =>
        error is DbException { SqlState: "40001" or "40P01" } || error.InnerException is { } inner && VendorRequestSerializationConflict(inner);

    public async Task<Result<FishMeatRegistrationRequestDto>> CompleteVendorRequestAsync(Guid id,
        CompleteFishMeatRegistrationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead || user.UserId is not { } actorId || actorId == Guid.Empty) return Result<FishMeatRegistrationRequestDto>.Forbidden();
        if (!VendorRequestsInstalled) return VendorRequestUnavailable();
        if (request.Registration is null || request.Registration.NpmAssociation is not null)
            return Result<FishMeatRegistrationRequestDto>.Failure("Complete the independent office registration; no NPM association is permitted here.", ResultStatus.Invalid);
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var row = await db.FishMeatRegistrationRequests.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);
            if (row is null) return Result<FishMeatRegistrationRequestDto>.NotFound();
            var fingerprint = AssociationHash(new { id, Actor = user.UserId, request });
            if (row.DecisionOperationId == request.ClientOperationId)
                return row.DecisionFingerprint == fingerprint ? Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(row))
                    : Result<FishMeatRegistrationRequestDto>.Failure("ReviewIntentConflict", ResultStatus.Conflict);
            if (row.Status != FishMeatRequestStatus.Submitted || row.Version != request.ExpectedVersion)
                return VendorRequestConflict();
            if (request.Registration.TaxYear != row.TaxYear || row.TaxYear != clock.PhilippineToday.Year
                || request.Registration.VendorType != row.VendorType || request.ClientOperationId == Guid.Empty)
                return Result<FishMeatRegistrationRequestDto>.Failure("Confirm the current tax year and proposed vendor type.", ResultStatus.Invalid);
            // The request, not a new button-click ID, owns registration creation across retries.
            var registered = await RegisterAsync(request.Registration with { ClientOperationId = row.Id }, ct);
            if (!registered.IsSuccess) return Result<FishMeatRegistrationRequestDto>.Failure(registered.Error!, registered.Status);
            if (!await db.FishMeatVendorRegistrations.AnyAsync(x => x.MunicipalityId == Tenant && x.Id == registered.Value!.Id
                && x.Status == VendorRegistrationStatus.Active && x.TaxYear == row.TaxYear && x.VendorType == row.VendorType, ct))
                return Result<FishMeatRegistrationRequestDto>.Failure("The completed registration is no longer active. Reconcile the office record.", ResultStatus.Conflict);
            row.Decide(request.ClientOperationId, fingerprint, request.ExpectedVersion, FishMeatRequestStatus.Approved,
                user.Username ?? "Head", request.Reason, clock.UtcNow, registered.Value!.Id, actorId);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(row));
        }
        catch (ArgumentException e) { return Result<FishMeatRegistrationRequestDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (InvalidOperationException e) { return Result<FishMeatRegistrationRequestDto>.Failure(e.Message, ResultStatus.Conflict); }
        catch (DbUpdateException) { return VendorRequestConflict(); }
        catch (DbException) { return VendorRequestConflict(); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    public Task<Result<FishMeatRegistrationRequestDto>> CancelVendorRequestAsync(Guid id, CancelFishMeatRegistrationRequest request, CancellationToken ct = default) =>
        DecideVendorRequestAsync(id, new(request.ClientOperationId, request.ExpectedVersion, FishMeatRequestStatus.Cancelled, "Cancelled by submitting collector"), true, ct);
    public Task<Result<FishMeatRegistrationRequestDto>> ReviewVendorRequestAsync(Guid id, ReviewFishMeatRegistrationRequest request, CancellationToken ct = default) =>
        DecideVendorRequestAsync(id, request, false, ct);

    private async Task<Result<FishMeatRegistrationRequestDto>> DecideVendorRequestAsync(Guid id, ReviewFishMeatRegistrationRequest request,
        bool cancel, CancellationToken ct)
    {
        if (cancel ? !await CanRequestVendor(ct) : !RegistryHead) return Result<FishMeatRegistrationRequestDto>.Forbidden();
        if (user.UserId is not { } actorId || actorId == Guid.Empty) return Result<FishMeatRegistrationRequestDto>.Forbidden();
        if (!VendorRequestsInstalled) return VendorRequestUnavailable();
        if (!cancel && request.Outcome == FishMeatRequestStatus.Cancelled)
            return Result<FishMeatRegistrationRequestDto>.Failure("Only the submitting collector may cancel.", ResultStatus.Invalid);
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var row = await db.FishMeatRegistrationRequests.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id
                && (!cancel || x.CollectorId == user.CollectorId), ct);
            if (row is null) return Result<FishMeatRegistrationRequestDto>.NotFound();
            var fingerprint = AssociationHash(new { id, Actor = user.UserId, Collector = user.CollectorId, request });
            if (row.DecisionOperationId == request.ClientOperationId)
                return row.DecisionFingerprint == fingerprint ? Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(row))
                    : Result<FishMeatRegistrationRequestDto>.Failure("ReviewIntentConflict", ResultStatus.Conflict);
            if (request.Outcome == FishMeatRequestStatus.Approved)
            {
                var vendor = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
                    && x.Id == request.RegistrationId, ct);
                if (vendor is null || vendor.Status != VendorRegistrationStatus.Active || vendor.TaxYear != row.TaxYear
                    || vendor.TaxYear != clock.PhilippineToday.Year || vendor.VendorType != row.VendorType)
                    return Result<FishMeatRegistrationRequestDto>.Failure("Complete and verify the current-year office registration first. Correct mismatched proposals by returning the request.", ResultStatus.Conflict);
            }
            row.Decide(request.ClientOperationId, fingerprint, request.ExpectedVersion, request.Outcome,
                user.Username ?? (cancel ? "Collector" : "Head"), request.Reason, clock.UtcNow, request.RegistrationId, actorId);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<FishMeatRegistrationRequestDto>.Success(VendorRequestDto(row));
        }
        catch (ArgumentException e) { return Result<FishMeatRegistrationRequestDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (InvalidOperationException e) { return Result<FishMeatRegistrationRequestDto>.Failure(e.Message, ResultStatus.Conflict); }
        catch (DbUpdateException) { return VendorRequestConflict(); }
        catch (DbException) { return VendorRequestConflict(); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }
}

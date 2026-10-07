using System.Data.Common;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class OfficeCollectionWorkflow
{
    private bool RegistryHead => TenantValid && user.IsAuthenticated && user.Role == "SuperAdmin";
    private static VendorRegistrationMutationResult Mutation(FishMeatVendorRegistration r, bool replay) =>
        new(ToDto(r), r.Status, r.PriorRegistrationId, r.ClosedOn, r.ClosedAtUtc, r.ClosedBy, r.CloseNote, replay);

    public async Task<Result<VendorRegistrationMutationResult>> CloseRegistrationAsync(Guid id,
        CloseVendorRegistrationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<VendorRegistrationMutationResult>.Forbidden();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (id == Guid.Empty || request.ClientOperationId == Guid.Empty || note?.Length > 300)
            return Result<VendorRegistrationMutationResult>.Failure("InvalidCloseIntent", ResultStatus.Invalid);
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var replay = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == Tenant && x.CloseClientOperationId == request.ClientOperationId, ct);
            if (replay is not null)
                return replay.Id == id && replay.CloseNote == note
                    ? Result<VendorRegistrationMutationResult>.Success(Mutation(replay, true))
                    : Result<VendorRegistrationMutationResult>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            var registration = await db.FishMeatVendorRegistrations.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);
            if (registration is null) return Result<VendorRegistrationMutationResult>.NotFound();
            if (registration.Status == VendorRegistrationStatus.Closed)
                return Result<VendorRegistrationMutationResult>.Failure("RegistrationClosed", ResultStatus.Conflict);
            registration.Close(request.ClientOperationId, clock.PhilippineToday, user.Username ?? "Head", note);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); committed = true;
            return Result<VendorRegistrationMutationResult>.Success(Mutation(registration, false));
        }
        catch (DbUpdateException) { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (DbException) { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (InvalidOperationException e) when (e.InnerException is DbUpdateException)
        { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    public async Task<Result<VendorRegistrationMutationResult>> RenewRegistrationAsync(Guid id,
        RenewVendorRegistrationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<VendorRegistrationMutationResult>.Forbidden();
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var source = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);
            if (source is null) return Result<VendorRegistrationMutationResult>.NotFound();
            FishMeatVendorRegistration candidate;
            try { candidate = FishMeatVendorRegistration.Renew(source, request.ClientOperationId, request.TaxYear,
                request.DisplayName, request.BusinessName, request.Address, request.Reference, user.Username ?? "Head"); }
            catch (ArgumentException e) { return Result<VendorRegistrationMutationResult>.Failure(e.Message, ResultStatus.Invalid); }
            var prior = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null)
                return prior.PriorRegistrationId == id && ToDto(prior) == ToDto(candidate) with { Id = prior.Id }
                    ? Result<VendorRegistrationMutationResult>.Success(Mutation(prior, true))
                    : Result<VendorRegistrationMutationResult>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            if (await db.FishMeatVendorRegistrations.AnyAsync(x => x.MunicipalityId == Tenant && x.PriorRegistrationId == id && x.TaxYear == request.TaxYear, ct))
                return Result<VendorRegistrationMutationResult>.Failure("RenewalAlreadyExists", ResultStatus.Conflict);
            db.FishMeatVendorRegistrations.Add(candidate);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); committed = true;
            return Result<VendorRegistrationMutationResult>.Success(Mutation(candidate, false));
        }
        catch (DbUpdateException) { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (DbException) { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (InvalidOperationException e) when (e.InnerException is DbUpdateException)
        { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    public async Task<Result<VendorRegistryManagement>> ManageRegistrationsAsync(int taxYear, int month,
        VendorRegistrationStatus? status = null, int? year = null, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin")) return Result<VendorRegistryManagement>.Forbidden();
        var activityYear = year ?? taxYear;
        if (taxYear is < 2000 or > 2200 || activityYear is < 2000 or > 2200 || month is < 1 or > 12 || status.HasValue && !Enum.IsDefined(status.Value))
            return Result<VendorRegistryManagement>.Failure("InvalidPeriod", ResultStatus.Invalid);
        var registrations = await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.TaxYear == taxYear
            && (status == null || x.Status == status)).OrderBy(x => x.DisplayName).ThenBy(x => x.Id).ToListAsync(ct);
        var from = new DateOnly(activityYear, month, 1);
        // Reuse canonical source activity, including reversal effects. Frozen registration IDs alone attribute money.
        var activity = await ActivityAsync(from, from.AddMonths(1).AddDays(-1), ct: ct);
        if (!activity.IsSuccess) return Result<VendorRegistryManagement>.Failure(activity.Error!, activity.Status);
        var vendorMoney = activity.Value!.Where(x => x.OperationCode is CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure)
            .ToLookup(x => x.VendorRegistrationId);
        var rows = registrations.Select(r => new VendorRegistrationManagementRow(ToDto(r), r.Status, r.CreatedBy, r.RecordedAtUtc,
            r.ClosedOn, r.ClosedAtUtc, r.ClosedBy, r.CloseNote, r.PriorRegistrationId,
            vendorMoney[r.Id].Where(x => x.OperationCode == CollectorOperationCodes.FishMeatVendorFee).Sum(x => x.NetAmount),
            vendorMoney[r.Id].Where(x => x.OperationCode == CollectorOperationCodes.WeightAndMeasure).Sum(x => x.NetAmount))).ToArray();
        return Result<VendorRegistryManagement>.Success(new(taxYear, activityYear, month, rows,
            vendorMoney[null].Where(x => x.OperationCode == CollectorOperationCodes.FishMeatVendorFee).Sum(x => x.NetAmount)));
    }
}

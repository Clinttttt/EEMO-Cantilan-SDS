using System.Data.Common;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class OfficeCollectionWorkflow
{
    public async Task<Result<VendorRegistrationMutationResult>> DeleteRegistrationAsync(Guid id,
        DeleteVendorRegistrationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<VendorRegistrationMutationResult>.Forbidden();
        if (id == Guid.Empty || request.ClientOperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 500)
            return Result<VendorRegistrationMutationResult>.Failure("InvalidDeletionEvidence", ResultStatus.Invalid);
        var reason = request.Reason.Trim();
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var replay = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == Tenant && x.DeleteClientOperationId == request.ClientOperationId, ct);
            if (replay is not null)
                return replay.Id == id && replay.DeleteReason == reason && replay.DeletedBy == (user.Username ?? "Head")
                    ? Result<VendorRegistrationMutationResult>.Success(Mutation(replay, true))
                    : Result<VendorRegistrationMutationResult>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            var registration = await db.FishMeatVendorRegistrations.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);
            if (registration is null) return Result<VendorRegistrationMutationResult>.NotFound();
            if (registration.Status == VendorRegistrationStatus.Deleted)
                return Result<VendorRegistrationMutationResult>.Failure("RegistrationDeleted", ResultStatus.Conflict);
            if (registration.Status != VendorRegistrationStatus.Closed)
                return Result<VendorRegistrationMutationResult>.Failure("CloseRegistrationBeforeDelete", ResultStatus.Conflict);
            // Pending proposals require an explicit reviewed decision, not silent cancellation by a registry action.
            if (await db.NpmRegistrationRequests.AnyAsync(x => x.MunicipalityId == Tenant && x.VendorRegistrationId == id
                && x.Status == NpmRegistrationRequestStatus.Submitted, ct))
                return Result<VendorRegistrationMutationResult>.Failure("ReviewPendingNpmRequestsBeforeDelete", ResultStatus.Conflict);
            var links = await db.FishMeatNpmAssociations.Where(x => x.MunicipalityId == Tenant
                && x.VendorRegistrationId == id && x.CorrectedAtUtc == null).ToListAsync(ct);
            foreach (var link in links)
                link.Correct(Guid.NewGuid(), $"Registration {id} deleted; see its deletion reason and audit evidence.", user.Username ?? "Head");
            registration.DeleteClosed(request.ClientOperationId, user.Username ?? "Head", reason, clock.UtcNow);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<VendorRegistrationMutationResult>.Success(Mutation(registration, false));
        }
        catch (ArgumentException e) { return Result<VendorRegistrationMutationResult>.Failure(e.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (DbException) { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (InvalidOperationException e) when (e.InnerException is DbUpdateException)
        { return Result<VendorRegistrationMutationResult>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }
}

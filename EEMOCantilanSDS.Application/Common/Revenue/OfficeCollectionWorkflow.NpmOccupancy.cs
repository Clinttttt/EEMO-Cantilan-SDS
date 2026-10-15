using System.Data.Common;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class OfficeCollectionWorkflow
{
    public async Task<Result<NpmVendorOccupancyResult>> CreateNpmVendorOccupancyAsync(
        CreateNpmVendorOccupancyRequest request, ISender sender, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<NpmVendorOccupancyResult>.Forbidden();
        var command = request.Occupancy;
        if (request.ClientOperationId == Guid.Empty || request.VendorRegistrationId == Guid.Empty || command is null
            || command.FacilityCode != FacilityCode.NPM || command.ContractDate is null
            || string.IsNullOrWhiteSpace(request.ConfirmationNote) || request.ConfirmationNote.Trim().Length > 500)
            return Result<NpmVendorOccupancyResult>.Failure("Complete the NPM tenancy and office confirmation.", ResultStatus.Invalid);
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var fingerprint = AssociationHash(new { Intent = "NpmOccupancyFromRegisteredVendor", request });
            var replay = await db.FishMeatNpmAssociations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
            if (replay is not null)
                return replay.RequestFingerprint == fingerprint
                    ? Result<NpmVendorOccupancyResult>.Success(new(replay.StallId, replay.ContractId, replay.Id, true))
                    : Result<NpmVendorOccupancyResult>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            var vendor = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == Tenant && x.Id == request.VendorRegistrationId, ct);
            if (vendor is null) return Result<NpmVendorOccupancyResult>.NotFound();
            if (vendor.Status != VendorRegistrationStatus.Active)
                return Result<NpmVendorOccupancyResult>.Failure("RegistrationClosed", ResultStatus.Conflict);
            if (vendor.DisplayName != request.ExpectedVendorName)
                return Result<NpmVendorOccupancyResult>.Failure("Vendor registration changed. Select it again.", ResultStatus.Conflict);
            var effective = DateOnly.FromDateTime(command.ContractDate.Value);
            if (vendor.TaxYear != effective.Year)
                return Result<NpmVendorOccupancyResult>.Failure("Select the vendor registration for the tenancy effectivity year.", ResultStatus.Invalid);
            var expectedSection = vendor.VendorType == FishMeatVendorType.Fish ? MarketSection.FishSection : MarketSection.MeatSection;
            if (command.Section != expectedSection && !request.ExceptionConfirmed)
                return Result<NpmVendorOccupancyResult>.Failure("Confirm the section differs from the Fish/Meat registration.", ResultStatus.Invalid);
            // Name matches are review warnings only, never identity. Require a deliberate duplicate review for every
            // create-from-registry operation, including when the selected vendor has no recorded association.
            if (!request.DuplicateReviewConfirmed)
                return Result<NpmVendorOccupancyResult>.Failure("Review existing NPM occupancies before creating another.", ResultStatus.Invalid);
            // Reuse the canonical command pipeline, including its validator, vacancy checks and rate rules. Both its
            // SaveChanges and the association insert are enclosed by this transaction; no cash writer is called.
            var created = await sender.Send(command, ct);
            if (!created.IsSuccess)
                return new(false, null, created.StatusCode, created.Error) { ValidationErrors = created.ValidationErrors };
            if (created.Value?.OccupancyId is not { } contractId)
                return Result<NpmVendorOccupancyResult>.Failure("The NPM writer did not return a verified occupancy.", ResultStatus.Conflict);
            var sources = await NpmVendorCandidatesAsync(vendor.TaxYear, true, true, ct);
            var source = sources.Value?.Rows.SingleOrDefault(x => x.ContractId == contractId && x.StallId == created.Value.Id);
            if (source is null) return Result<NpmVendorOccupancyResult>.Failure("NPM occupancy could not be verified.", ResultStatus.Conflict);
            var link = FishMeatNpmAssociation.Confirm(Tenant, vendor.Id, source.ContractId, source.StallId,
                request.ClientOperationId, fingerprint, source.Occupant, source.SourceVersion, request.ConfirmationNote, user.Username ?? "Head");
            db.FishMeatNpmAssociations.Add(link);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); committed = true;
            return Result<NpmVendorOccupancyResult>.Success(new(link.StallId, link.ContractId, link.Id, false));
        }
        catch (FluentValidation.ValidationException e)
        {
            return Result<NpmVendorOccupancyResult>.ValidationFailure(e.Errors.GroupBy(x => x.PropertyName)
                .ToDictionary(x => x.Key, x => x.Select(v => v.ErrorMessage).ToArray()));
        }
        catch (ArgumentException e) { return Result<NpmVendorOccupancyResult>.Failure(e.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { return Result<NpmVendorOccupancyResult>.Failure("Registration concurrency conflict. Refresh before retrying.", ResultStatus.Conflict); }
        catch (DbException) { return Result<NpmVendorOccupancyResult>.Failure("Registration concurrency conflict. Refresh before retrying.", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }
}

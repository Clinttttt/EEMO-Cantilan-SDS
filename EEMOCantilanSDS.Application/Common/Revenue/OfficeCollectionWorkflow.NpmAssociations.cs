using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;
public sealed partial class OfficeCollectionWorkflow
{
    private static string AssociationHash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static NpmVendorAssociationDto AssociationDto(FishMeatNpmAssociation x) => new(x.Id, x.VendorRegistrationId,
        x.ContractId, x.StallId, x.OccupantSnapshot, x.ConfirmedBy, x.ConfirmedAtUtc, x.ConfirmationNote,
        x.CorrectedAtUtc, x.CorrectedBy, x.CorrectionReason);

    public async Task<Result<NpmVendorCandidates>> NpmVendorCandidatesAsync(int taxYear, bool historical = false,
        bool exceptions = false, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin")) return Result<NpmVendorCandidates>.Forbidden();
        if (taxYear is < 2000 or > 2200) return Result<NpmVendorCandidates>.Failure("InvalidPeriod", ResultStatus.Invalid);
        var sources = await (from c in db.Contracts.AsNoTracking()
            join s in db.Stalls.AsNoTracking() on c.StallId equals s.Id
            join f in db.Facilities.AsNoTracking() on s.FacilityId equals f.Id
            where c.MunicipalityId == Tenant && s.MunicipalityId == Tenant && f.MunicipalityId == Tenant
                && f.Code == FacilityCode.NPM && c.EffectivityDate <= new DateOnly(taxYear, 12, 31)
            select new { c, s, f }).ToListAsync(ct);
        var vendors = await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.TaxYear == taxYear).ToListAsync(ct);
        var vendorIds = vendors.Select(x => x.Id).ToArray();
        var links = await db.FishMeatNpmAssociations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && vendorIds.Contains(x.VendorRegistrationId)).ToListAsync(ct);
        var rows = sources.Where(x => (historical || x.c.IsActive && x.s.Status == StallStatus.Active)
            && (exceptions || x.s.Section is MarketSection.FishSection or MarketSection.MeatSection)).Select(x =>
        {
            FishMeatVendorType? type = x.s.Section switch { MarketSection.FishSection => FishMeatVendorType.Fish, MarketSection.MeatSection => FishMeatVendorType.Meat, _ => null };
            var area = x.s.Section switch { MarketSection.FishSection => x.f.FishSectionLabel ?? "Fish Area",
                MarketSection.MeatSection => x.f.MeatSectionLabel ?? "Meat Area", MarketSection.VegetableArea => x.f.VegetableSectionLabel ?? "Vegetable Area", _ => x.s.CustomSectionName ?? "Unspecified area" };
            var version = AssociationHash(new { x.c.Id, x.c.StallId, x.c.ActualOccupant, x.c.NameOnContract,
                x.c.EffectivityDate, x.c.IsActive, ContractUpdatedAt = x.c.UpdatedAt, x.s.Status, x.s.Section, x.s.CustomSectionName, StallUpdatedAt = x.s.UpdatedAt });
            return new NpmVendorCandidate(x.c.Id, x.s.Id, x.s.StallNo, x.c.ActualOccupant, area,
                x.c.IsActive && x.s.Status == StallStatus.Active, x.c.EffectivityDate, type, version,
                links.Where(l => l.ContractId == x.c.Id).Select(AssociationDto).ToArray(),
                vendors.Where(v => string.Equals(v.DisplayName.Trim(), x.c.ActualOccupant.Trim(), StringComparison.OrdinalIgnoreCase)).Select(ToDto).ToArray());
        }).OrderBy(x => !x.Active).ThenBy(x => x.Area).ThenBy(x => x.StallNo).ThenBy(x => x.ContractId).ToArray();
        // Default count excludes history and manual exceptions; an exact active link is the only proof of resolution.
        var count = rows.Count(x => x.Active && x.SuggestedType.HasValue && !x.Links.Any(l => !l.CorrectedAtUtc.HasValue));
        return Result<NpmVendorCandidates>.Success(new(taxYear, count, rows));
    }

    public async Task<Result<IReadOnlyList<NpmVendorAssociationDto>>> NpmVendorLinksAsync(Guid vendorId, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin")) return Result<IReadOnlyList<NpmVendorAssociationDto>>.Forbidden();
        if (!await db.FishMeatVendorRegistrations.AnyAsync(x => x.Id == vendorId && x.MunicipalityId == Tenant, ct)) return Result<IReadOnlyList<NpmVendorAssociationDto>>.NotFound();
        return Result<IReadOnlyList<NpmVendorAssociationDto>>.Success((await db.FishMeatNpmAssociations.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && x.VendorRegistrationId == vendorId).OrderBy(x => x.ConfirmedAtUtc).ToListAsync(ct)).Select(AssociationDto).ToArray());
    }

    private async Task<(NpmVendorCandidate? Source, string? Error)> ValidateNpmAssociation(FishMeatVendorRegistration vendor,
        ConfirmNpmVendorAssociationRequest request, bool creating, CancellationToken ct)
    {
        var candidates = await NpmVendorCandidatesAsync(vendor.TaxYear, true, true, ct);
        var source = candidates.Value?.Rows.SingleOrDefault(x => x.ContractId == request.ContractId && x.StallId == request.StallId);
        if (source is null) return (null, "InvalidNpmOccupancy");
        if (source.SourceVersion != request.SourceVersion) return (null, "NpmOccupancyChanged");
        if (string.IsNullOrWhiteSpace(request.ConfirmationNote) || request.ConfirmationNote.Trim().Length > 500) return (null, "RequiresOfficeConfirmation");
        if ((!source.Active || source.SuggestedType != vendor.VendorType) && !request.ExceptionConfirmed) return (null, "RequiresExceptionConfirmation");
        if (vendor.Status != VendorRegistrationStatus.Active) return (null, "RegistrationClosed");
        if (creating && (source.Links.Any(x => x.CorrectedAtUtc == null) || source.PossibleMatches.Count > 0) && !request.DuplicateReviewConfirmed)
            return (null, "RequiresDuplicateReview");
        return (source, null);
    }

    private async Task<Result<FishMeatVendorRegistrationDto>> RegisterFromNpmAsync(RegisterFishMeatVendorRequest request, CancellationToken ct)
    {
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var link = request.NpmAssociation!;
            if (link.ClientOperationId != request.ClientOperationId) return Result<FishMeatVendorRegistrationDto>.Failure("RegistrationIntentConflict", ResultStatus.Invalid);
            var fingerprint = AssociationHash(request);
            var replay = await db.FishMeatNpmAssociations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
            if (replay is not null)
            {
                if (replay.RequestFingerprint != fingerprint) return Result<FishMeatVendorRegistrationDto>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
                var saved = await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync(x => x.Id == replay.VendorRegistrationId && x.MunicipalityId == Tenant, ct);
                return Result<FishMeatVendorRegistrationDto>.Success(ToDto(saved));
            }
            if (await db.FishMeatVendorRegistrations.AnyAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct))
                return Result<FishMeatVendorRegistrationDto>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            var vendor = FishMeatVendorRegistration.Register(Tenant, request.ClientOperationId, request.TaxYear, request.VendorType,
                request.RegistrationKind, request.DisplayName, request.BusinessName, request.Address, request.Reference, user.Username ?? "Head",
                request.LastName, request.FirstName, request.MiddleName, request.NameExtension, request.Sex, request.NatureOfBusiness);
            var validated = await ValidateNpmAssociation(vendor, link, true, ct);
            if (validated.Error is { } error) return Result<FishMeatVendorRegistrationDto>.Failure(error, ResultStatus.Conflict);
            db.FishMeatVendorRegistrations.Add(vendor);
            db.FishMeatNpmAssociations.Add(FishMeatNpmAssociation.Confirm(Tenant, vendor.Id, link.ContractId, link.StallId,
                link.ClientOperationId, fingerprint, validated.Source!.Occupant, link.SourceVersion, link.ConfirmationNote, user.Username ?? "Head"));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<FishMeatVendorRegistrationDto>.Success(ToDto(vendor));
        }
        catch (ArgumentException e) { return Result<FishMeatVendorRegistrationDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { return Result<FishMeatVendorRegistrationDto>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (DbException) { return Result<FishMeatVendorRegistrationDto>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    public async Task<Result<NpmVendorAssociationDto>> LinkNpmVendorAsync(Guid vendorId, ConfirmNpmVendorAssociationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<NpmVendorAssociationDto>.Forbidden();
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var fingerprint = AssociationHash(new { vendorId, request });
            var replay = await db.FishMeatNpmAssociations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
            if (replay is not null) return replay.RequestFingerprint == fingerprint ? Result<NpmVendorAssociationDto>.Success(AssociationDto(replay))
                : Result<NpmVendorAssociationDto>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            var vendor = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == vendorId && x.MunicipalityId == Tenant, ct);
            if (vendor is null) return Result<NpmVendorAssociationDto>.NotFound();
            var validated = await ValidateNpmAssociation(vendor, request, false, ct);
            if (validated.Error is { } error) return Result<NpmVendorAssociationDto>.Failure(error, ResultStatus.Conflict);
            if (validated.Source!.Links.Any(x => x.VendorRegistrationId == vendorId && x.CorrectedAtUtc == null)) return Result<NpmVendorAssociationDto>.Failure("AssociationAlreadyExists", ResultStatus.Conflict);
            var link = FishMeatNpmAssociation.Confirm(Tenant, vendorId, request.ContractId, request.StallId, request.ClientOperationId,
                fingerprint, validated.Source.Occupant, request.SourceVersion, request.ConfirmationNote, user.Username ?? "Head");
            db.FishMeatNpmAssociations.Add(link);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            var linked = AssociationDto(link);
            await ResolveRequestsAnsweredByLinkAsync(vendorId, link.StallId, link.Id, ct);
            return Result<NpmVendorAssociationDto>.Success(linked);
        }
        catch (ArgumentException e) { return Result<NpmVendorAssociationDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { return Result<NpmVendorAssociationDto>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (DbException) { return Result<NpmVendorAssociationDto>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }

    public async Task<Result<NpmVendorAssociationDto>> CorrectNpmVendorLinkAsync(Guid id, CorrectNpmVendorAssociationRequest request, CancellationToken ct = default)
    {
        if (!RegistryHead) return Result<NpmVendorAssociationDto>.Forbidden();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) return Result<NpmVendorAssociationDto>.Failure("InvalidAssociationEvidence", ResultStatus.Invalid);
        var committed = false;
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var replay = await db.FishMeatNpmAssociations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.CorrectionOperationId == request.ClientOperationId, ct);
            if (replay is not null) return replay.Id == id && replay.CorrectionReason == request.Reason.Trim()
                ? Result<NpmVendorAssociationDto>.Success(AssociationDto(replay)) : Result<NpmVendorAssociationDto>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
            var link = await db.FishMeatNpmAssociations.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);
            if (link is null) return Result<NpmVendorAssociationDto>.NotFound();
            link.Correct(request.ClientOperationId, request.Reason, user.Username ?? "Head");
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
            return Result<NpmVendorAssociationDto>.Success(AssociationDto(link));
        }
        catch (ArgumentException e) { return Result<NpmVendorAssociationDto>.Failure(e.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { return Result<NpmVendorAssociationDto>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        catch (DbException) { return Result<NpmVendorAssociationDto>.Failure("RegistryConcurrencyConflict", ResultStatus.Conflict); }
        finally { if (!committed) db.ChangeTracker.Clear(); }
    }
}

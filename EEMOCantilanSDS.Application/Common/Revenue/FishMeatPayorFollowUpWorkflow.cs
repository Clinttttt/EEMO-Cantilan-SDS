using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Office follow-up for direct Fish/Meat Vendor Fee Collections recorded against a payer name with no registered vendor. The payment is already
/// posted and counted; this only lets the Head identify who paid. It READS the canonical Collections, and its single write is an append-only
/// link from the Collection to a registration the Head chose (or just created). It never edits the Collection, its SRC, payer snapshot, amount,
/// remittance or accountable document, never issues a second SRC, and never infers identity from a matching name.
/// </summary>
public sealed class FishMeatPayorFollowUpWorkflow(IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor municipality, OfficeCollectionWorkflow office)
{
    public const int MaxPage = 100;

    private bool TryTenant(out Guid tenant)
    {
        tenant = municipality.MunicipalityId;
        return user.IsAuthenticated && user.Role is "SuperAdmin" or "Admin" && tenant != Guid.Empty && (!user.MunicipalityId.HasValue || user.MunicipalityId == tenant);
    }

    /// <summary>Collections whose Vendor Fee line names no registered vendor: the Collector recorded a payer name only.</summary>
    public static IQueryable<Collection> PayerOnlyCollections(IAppDbContext db, Guid tenant) =>
        db.Collections.AsNoTracking().Where(c => c.MunicipalityId == tenant && c.CollectorId != null
            && db.CollectionLines.Any(l => l.MunicipalityId == tenant && l.CollectionId == c.Id && l.SourceId == null && l.SourceKind == null
                && db.RevenueClassifications.Any(k => k.Id == l.RevenueClassificationId && k.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee)));

    private IQueryable<Collection> Unresolved(Guid tenant)
    {
        var reversed = db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.FinancialEffectAmount < 0m).Select(x => x.OriginalCollectionId);
        var linked = db.FishMeatDirectCollectionLinks.AsNoTracking().Where(x => x.MunicipalityId == tenant).Select(x => x.CollectionId);
        return PayerOnlyCollections(db, tenant).Where(c => !reversed.Contains(c.Id) && !linked.Contains(c.Id));
    }

    public async Task<Result<FishMeatPayorFollowUpPageDto>> ListAsync(string? search, int skip, int take, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenant)) return Result<FishMeatPayorFollowUpPageDto>.Forbidden();
        var q = Unresolved(tenant);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(c => c.ReferenceCode.ToLower().Contains(term) || (c.PayerName != null && c.PayerName.ToLower().Contains(term)));
        }
        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(c => c.RecordedAtUtc).ThenByDescending(c => c.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take <= 0 ? 25 : take, 1, MaxPage)).ToListAsync(ct);
        return Result<FishMeatPayorFollowUpPageDto>.Success(new(await RowsAsync(tenant, page, ct), total));
    }

    private async Task<IReadOnlyList<FishMeatPayorFollowUpRowDto>> RowsAsync(Guid tenant, List<Collection> page, CancellationToken ct)
    {
        var collectorIds = page.Where(c => c.CollectorId.HasValue).Select(c => c.CollectorId!.Value).Distinct().ToArray();
        var collectors = await db.CollectorUsers.AsNoTracking().Where(c => c.MunicipalityId == tenant && collectorIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        return page.Select(c => new FishMeatPayorFollowUpRowDto(c.Id, c.ReferenceCode, c.BusinessDate, c.RecordedAtUtc,
            string.IsNullOrWhiteSpace(c.PayerName) ? "No payer named" : c.PayerName, c.TotalAmount,
            c.CollectorId is { } id ? collectors.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<Result<FishMeatPayorFollowUpDetailDto>> DetailAsync(Guid collectionId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenant)) return Result<FishMeatPayorFollowUpDetailDto>.Forbidden();
        var collection = await PayerOnlyCollections(db, tenant).SingleOrDefaultAsync(c => c.Id == collectionId, ct);
        if (collection is null) return Result<FishMeatPayorFollowUpDetailDto>.NotFound();
        var row = (await RowsAsync(tenant, [collection], ct))[0];
        var reversed = await db.CollectionCorrections.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.OriginalCollectionId == collectionId && x.FinancialEffectAmount < 0m, ct);
        var document = await db.AccountableDocuments.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.CollectionId == collectionId)
            .Select(x => new { x.DocumentNumber, x.InstrumentType }).FirstOrDefaultAsync(ct);
        var link = await db.FishMeatDirectCollectionLinks.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.CollectionId == collectionId).SingleOrDefaultAsync(ct);
        string? linkedVendor = null;
        if (link is not null)
            linkedVendor = await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.Id == link.VendorRegistrationId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);

        // Suggestions only: active registrations of the Collection's own tax year whose name contains, or is contained in, the recorded payer name.
        var year = collection.BusinessDate.Year;
        var payer = (collection.PayerName ?? "").Trim().ToLower();
        var suggestions = new List<FishMeatPayorSuggestionDto>();
        if (payer.Length >= 3)
        {
            var matches = await db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.TaxYear == year && x.Status == VendorRegistrationStatus.Active
                && (x.DisplayName.ToLower().Contains(payer) || payer.Contains(x.DisplayName.ToLower()))).OrderBy(x => x.DisplayName).Take(8).ToListAsync(ct);
            suggestions.AddRange(matches.Select(x => new FishMeatPayorSuggestionDto(
                new(x.Id, x.TaxYear, x.VendorType, x.RegistrationKind, x.DisplayName, x.BusinessName, x.Address, x.Reference), "Similar name; confirm it is the same vendor")));
        }
        return Result<FishMeatPayorFollowUpDetailDto>.Success(new(row, document?.DocumentNumber, document?.InstrumentType.ToString(), reversed ? "Reversed" : "Posted",
            year, suggestions, linkedVendor, link?.LinkedAtUtc, link?.ActorName));
    }

    public async Task<Result<FishMeatPayorResolveResult>> ResolveAsync(FishMeatPayorResolveRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenant) || user.Role != "SuperAdmin" || user.UserId is not { } actorId || actorId == Guid.Empty)
            return Result<FishMeatPayorResolveResult>.Forbidden();
        if (request.ClientOperationId == Guid.Empty || request.CollectionId == Guid.Empty || (request.VendorRegistrationId.HasValue == (request.Register is not null)))
            return Result<FishMeatPayorResolveResult>.Failure("InvalidIntent", ResultStatus.Invalid);
        if (request.Note?.Trim().Length > 300) return Result<FishMeatPayorResolveResult>.Failure("Use a note of at most 300 characters.", ResultStatus.Invalid);

        var collection = await PayerOnlyCollections(db, tenant).SingleOrDefaultAsync(c => c.Id == request.CollectionId, ct);
        if (collection is null) return Result<FishMeatPayorResolveResult>.NotFound();

        // A retry of the same operation answers with what it already recorded: one link, never a second.
        var prior = await db.FishMeatDirectCollectionLinks.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.CollectionId != request.CollectionId) return Result<FishMeatPayorResolveResult>.Failure("This operation id was already used for a different link.", ResultStatus.Conflict);
            var recorded = await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync(x => x.MunicipalityId == tenant && x.Id == prior.VendorRegistrationId, ct);
            return Result<FishMeatPayorResolveResult>.Success(new(collection.Id, Dto(recorded), request.Register is not null));
        }
        if (await db.FishMeatDirectCollectionLinks.AnyAsync(x => x.MunicipalityId == tenant && x.CollectionId == collection.Id, ct))
            return Result<FishMeatPayorResolveResult>.Failure("This collection is already linked to a vendor.", ResultStatus.Conflict);
        if (await db.CollectionCorrections.AnyAsync(x => x.MunicipalityId == tenant && x.OriginalCollectionId == collection.Id && x.FinancialEffectAmount < 0m, ct))
            return Result<FishMeatPayorResolveResult>.Failure("This collection was reversed; there is nothing to link.", ResultStatus.Conflict);

        // Registering a previously unnamed payor and linking the posted Collection must
        // commit together. A failed or concurrent link cannot leave an orphan registration.
        await using var registrationTx = request.Register is not null
            ? await db.BeginSerializableTransactionAsync(ct) : null;
        FishMeatVendorRegistrationDto vendor;
        if (request.Register is { } register)
        {
            var created = await office.RegisterAsync(new(request.ClientOperationId, collection.BusinessDate.Year, register.VendorType, register.RegistrationKind,
                register.DisplayName, register.BusinessName, register.Address, null, null,
                register.LastName, register.FirstName, register.MiddleName, register.NameExtension,
                register.Sex, register.NatureOfBusiness), ct);
            if (!created.IsSuccess) return Result<FishMeatPayorResolveResult>.Failure(created.Error ?? "The vendor could not be registered.", created.Status);
            vendor = created.Value!;
        }
        else
        {
            var existing = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.Id == request.VendorRegistrationId, ct);
            if (existing is null) return Result<FishMeatPayorResolveResult>.NotFound();
            if (existing.TaxYear != collection.BusinessDate.Year)
                return Result<FishMeatPayorResolveResult>.Failure("Choose a registration for the year of this collection.", ResultStatus.Conflict);
            if (existing.Status != VendorRegistrationStatus.Active)
                return Result<FishMeatPayorResolveResult>.Failure("RegistrationClosed", ResultStatus.Conflict);
            vendor = Dto(existing);
        }

        try
        {
            db.FishMeatDirectCollectionLinks.Add(FishMeatDirectCollectionLink.Confirm(tenant, collection.Id, vendor.Id, request.ClientOperationId, request.Note, actorId, user.Username ?? "Head"));
            await db.SaveChangesAsync(ct);
            if (registrationTx is not null) await registrationTx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Result<FishMeatPayorResolveResult>.Failure("This collection was just linked by someone else.", ResultStatus.Conflict);
        }
        return Result<FishMeatPayorResolveResult>.Success(new(collection.Id, vendor, request.Register is not null));
    }

    private static FishMeatVendorRegistrationDto Dto(FishMeatVendorRegistration x) =>
        new(x.Id, x.TaxYear, x.VendorType, x.RegistrationKind, x.DisplayName, x.BusinessName, x.Address, x.Reference);
}

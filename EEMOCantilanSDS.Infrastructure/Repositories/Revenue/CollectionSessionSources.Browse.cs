using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

// Partial of CollectionSessionSources: the read-only registered-source browser behind Mobile New Collection. It lists the same source identities
// the existing search finds — an occupancy, a source-owned space account, a registered fish/meat vendor — but as bounded, category-filtered
// pages, so the collector sees the sources assigned to them without having to type a name first. It answers WHO may be collected from; what each
// can be collected for is still the server's own per-source discovery, never inferred here. No amount, balance or status is computed.
public sealed partial class CollectionSessionSources
{
    private static readonly (string Key, string Label)[] CategoryLabels =
    [
        (CollectionSourceCategories.FishMeat, "Fish / Meat"),
        (CollectionSourceCategories.Npm, "NPM"),
        (CollectionSourceCategories.Tcc, "TCC"), (CollectionSourceCategories.Ncc, "NCC"),
        (CollectionSourceCategories.Bbq, "BBQ"), (CollectionSourceCategories.Ice, "Ice Plant"),
        (CollectionSourceCategories.Kanmanggay, "Kanmanggay"),
        (CollectionSourceCategories.VegetableFruit, "Vegetable / Fruits"),
        (CollectionSourceCategories.FiestaAraw, "Fiesta / Araw"),
    ];

    public async Task<CollectionSourcePage> BrowseSourcesAsync(string? category, string? search, int skip, int take, CancellationToken ct)
    {
        var today = clock.PhilippineToday;
        var term = (search ?? "").Trim().ToLowerInvariant();

        // What this collector is assigned to, and nothing more. A category exists only where a real reader exists AND the assignment allows it.
        var facilities = await db.CollectorUsers.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == user.CollectorId && x.IsActive)
            .SelectMany(x => x.FacilityAssignments.Select(a => a.FacilityCode)).ToListAsync(ct);
        var assigned = await db.CollectorOperationAssignments.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.CollectorId == user.CollectorId)
            .Select(x => x.OperationCode).ToListAsync(ct);

        // Facility categories follow the tenant's active facilities, regardless of their billing archetype.
        facilities = await db.Facilities.AsNoTracking().Where(f => f.MunicipalityId == Tenant && f.IsActive && facilities.Contains(f.Code))
            .Select(f => f.Code).ToListAsync(ct);
        var available = new List<string>();
        if (assigned.Contains(CollectorOperationCodes.FishMeatVendorFee) || assigned.Contains(CollectorOperationCodes.WeightAndMeasure)) available.Add(CollectionSourceCategories.FishMeat);
        foreach (var facility in new[] { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE })
            if (facilities.Contains(facility)) available.Add(facility.ToString().ToLowerInvariant());
        var spaceKinds = SpaceKindsFor(assigned);
        if (spaceKinds.Contains(ObligationKind.KanmanggaySpaceRental)) available.Add(CollectionSourceCategories.Kanmanggay);
        if (spaceKinds.Contains(ObligationKind.VegetableFruitSpaceRental)) available.Add(CollectionSourceCategories.VegetableFruit);
        if (spaceKinds.Contains(ObligationKind.FiestaArawLotRental)) available.Add(CollectionSourceCategories.FiestaAraw);
        var categories = CategoryLabels.Where(c => available.Contains(c.Key)).Select(c => new CollectionSourceCategoryDto(c.Key, c.Label)).ToArray();

        var rentalKeys = new[] { "npm", "tcc", "ncc", "bbq", "ice" };
        var wanted = string.IsNullOrWhiteSpace(category) || category == "all" ? available
            : category == CollectionSourceCategories.Rentals ? available.Where(rentalKeys.Contains).ToList()
            : category == CollectionSourceCategories.Spaces ? available.Where(c => !rentalKeys.Contains(c) && c != CollectionSourceCategories.FishMeat).ToList()
            : available.Where(c => c == category).ToList();
        if (wanted.Count == 0) return new CollectionSourcePage([], 0, categories);

        // "All" is the categories in their fixed order, each in its OWN stable order. Nothing is merged across categories by name, so no
        // collation difference (punctuation, accents) can move a row across a page boundary: the skip is simply walked through the categories'
        // totals, and each category is asked only for the part of the window that falls inside it.
        var rows = new List<CollectionSourceSearchResult>();
        var total = 0;
        var skipLeft = skip;
        var takeLeft = take;
        foreach (var key in wanted)
        {
            var (items, n) = key switch
            {
                CollectionSourceCategories.FishMeat => await FishMeatPageAsync(term, skipLeft, takeLeft, today, ct),
                CollectionSourceCategories.Npm => await OccupancyPageAsync(term, facilities.Where(f => f == FacilityCode.NPM).ToList(), skipLeft, takeLeft, today, ct),
                CollectionSourceCategories.Tcc or CollectionSourceCategories.Ncc or CollectionSourceCategories.Bbq or CollectionSourceCategories.Ice =>
                    await OccupancyPageAsync(term, facilities.Where(f => f.ToString().Equals(key, StringComparison.OrdinalIgnoreCase)).ToList(), skipLeft, takeLeft, today, ct),
                _ => await SpacePageAsync(term, spaceKinds.Where(k => key == CollectionSourceCategories.Kanmanggay ? k == ObligationKind.KanmanggaySpaceRental
                    : key == CollectionSourceCategories.VegetableFruit ? k == ObligationKind.VegetableFruitSpaceRental : k == ObligationKind.FiestaArawLotRental).ToArray(), skipLeft, takeLeft, today, ct),
            };
            rows.AddRange(items);
            total += n;
            skipLeft = Math.Max(0, skipLeft - n);
            takeLeft -= items.Count;
        }
        return new CollectionSourcePage(rows, total, categories);
    }

    private static List<ObligationKind> SpaceKindsFor(IReadOnlyCollection<string> assigned)
    {
        var kinds = new List<ObligationKind>();
        if (assigned.Contains(CollectorOperationCodes.KanmanggaySpaceRental)) kinds.Add(ObligationKind.KanmanggaySpaceRental);
        if (assigned.Contains(CollectorOperationCodes.VegetableFruitSpaceRental)) kinds.Add(ObligationKind.VegetableFruitSpaceRental);
        if (assigned.Contains(CollectorOperationCodes.FiestaArawLotRental)) kinds.Add(ObligationKind.FiestaArawLotRental);
        return kinds;
    }

    // Registered fish / meat vendors of the current tax year that are still active — the same set the finder offers.
    private async Task<(IReadOnlyList<CollectionSourceSearchResult>, int)> FishMeatPageAsync(string term, int skip, int take, DateOnly today, CancellationToken ct)
    {
        var query = db.FishMeatVendorRegistrations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.TaxYear == today.Year
            && x.Status == VendorRegistrationStatus.Active && (term == "" || x.DisplayName.ToLower().Contains(term)));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (rows.Select(x => new CollectionSourceSearchResult(new(SourceIdentityKind.FishMeatVendorRegistration, x.Id), x.DisplayName,
            x.VendorType + " · " + x.TaxYear, CollectorOperationCodes.FishMeatVendorFee, x.TaxYear, x.VendorType)).ToArray(), total);
    }

    // Occupancies of the assigned facilities: the lessee actually answering for the stall this month, by the stall's own rule. The page is cut
    // AFTER that rule is applied, so the total and the paging describe exactly the rows a collector can be shown — a stall whose old contract
    // matched but whose current occupant does not, or a vacant stall, is never counted. The candidate set is bounded by the assigned facilities.
    private async Task<(IReadOnlyList<CollectionSourceSearchResult>, int)> OccupancyPageAsync(
        string term, IReadOnlyCollection<FacilityCode> facilities, int skip, int take, DateOnly today, CancellationToken ct)
    {
        if (facilities.Count == 0) return ([], 0);
        // The rule needs a stall's whole contract history, so stalls are read in fixed-size keyset chunks and only the small result rows are kept:
        // memory is bounded by the matches, not by every stall graph, and the order of the chunks never affects the answer.
        const int Chunk = 200;
        var candidates = db.Stalls.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && facilities.Contains(x.Facility!.Code)
                && (term == "" || x.StallNo.ToLower().Contains(term)
                    || x.Contracts.Any(c => c.ActualOccupant != null && c.ActualOccupant.ToLower().Contains(term))));
        var rows = new List<CollectionSourceSearchResult>();
        var after = Guid.Empty;
        while (true)
        {
            var stalls = await candidates.Where(x => x.Id.CompareTo(after) > 0).OrderBy(x => x.Id).Take(Chunk)
                .Include(x => x.Facility).Include(x => x.Contracts).ToListAsync(ct);
            if (stalls.Count == 0) break;
            foreach (var stall in stalls)
            {
                var occupancy = stall.OccupancyAnsweringForMonth(today.Year, today.Month, today)?.Contract;
                if (occupancy?.ActualOccupant is { } name
                    && (term == "" || name.ToLowerInvariant().Contains(term) || stall.StallNo.ToLowerInvariant().Contains(term)))
                    rows.Add(new(new(SourceIdentityKind.Occupancy, occupancy.Id), name, $"{stall.Facility!.Name} · Stall {stall.StallNo}", "FACILITY_" + stall.Facility.Code,
                        SectionLabel: stall.Section is { } section ? stall.Facility.SectionLabel(section) ?? (section switch { MarketSection.FishSection => "Fish Area", MarketSection.MeatSection => "Meat Area", MarketSection.VegetableArea => "Vegetable Area", _ => stall.CustomSectionName }) : stall.CustomSectionName));
            }
            after = stalls[^1].Id;
        }
        var ordered = rows.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Identity.Id).ToList();
        return (ordered.Skip(skip).Take(take).ToArray(), ordered.Count);
    }

    // Assigned current space accounts, plus registered future event lots shown as non-selectable until their event date.
    private async Task<(IReadOnlyList<CollectionSourceSearchResult>, int)> SpacePageAsync(
        string term, IReadOnlyCollection<ObligationKind> kinds, int skip, int take, DateOnly today, CancellationToken ct)
    {
        var query = db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == Tenant && kinds.Contains(x.Kind)
            && ((x.ActiveFrom <= today && (x.ActiveTo == null || x.ActiveTo >= today))
                || (x.Kind == ObligationKind.FiestaArawLotRental && x.EventDate > today && x.ActiveTo == null))
            && (term == "" || x.SubjectLabel.ToLower().Contains(term)
                || (x.ActualOccupant != null && x.ActualOccupant.ToLower().Contains(term))
                || (x.PayorId != null && db.Payors.Any(p => p.MunicipalityId == Tenant
                    && p.Id == x.PayorId && p.DisplayName.ToLower().Contains(term)))));
        var total = await query.CountAsync(ct);
        var accounts = await query.OrderBy(x => x.SubjectLabel).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(Tenant, accounts, today, ct);
        var linkedIds = accounts.Where(a => a.PayorId.HasValue).Select(a => a.PayorId!.Value).Distinct().ToArray();
        var payorNames = await db.Payors.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && linkedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var results = accounts.Select(account => new CollectionSourceSearchResult(new(SourceIdentityKind.SpaceAccount, account.Id),
            quotes.FirstOrDefault(x => x.AccountId == account.Id)?.PayerName ?? account.ActualOccupant ?? (account.PayorId is { } id ? payorNames.GetValueOrDefault(id) : null) ?? account.SubjectLabel,
            account.Kind == ObligationKind.VegetableFruitSpaceRental ? $"Vegetable / Fruits · Space {account.SubjectLabel}" :
            account.Kind == ObligationKind.KanmanggaySpaceRental ? $"Kanmanggay · Space {account.SubjectLabel}" : $"Fiesta / Araw · Lot {account.SubjectLabel}",
            ObligationAccount.OperationCodeFor(account.Kind)!,
            CanSelect: account.Kind != ObligationKind.FiestaArawLotRental || account.ActiveFrom <= today,
            AvailableOn: account.Kind == ObligationKind.FiestaArawLotRental && account.ActiveFrom > today ? account.EventDate : null)).ToArray();
        return (results, total);
    }
}

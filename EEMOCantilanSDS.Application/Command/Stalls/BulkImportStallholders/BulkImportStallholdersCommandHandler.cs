using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;

public class BulkImportStallholdersCommandHandler(
    IStallRepository stallRepo,
    IFacilityRepository facilityRepo,
    IPayorRepository payorRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext, IClock clock) : IRequestHandler<BulkImportStallholdersCommand, Result<BulkImportResultDto>>
{
    private const string Actor = "Admin"; // matches CreateStallCommandHandler (no per-request user attribution)

    public async Task<Result<BulkImportResultDto>> Handle(BulkImportStallholdersCommand request, CancellationToken ct)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, ct);
        if (facility is null)
            return Result<BulkImportResultDto>.NotFound();

        var isNpm = request.FacilityCode == FacilityCode.NPM;
        var section = isNpm ? request.Section : null;
        // NPM custom section (Section null + a name): every imported row lands in this custom section,
        // billed flat-daily like Vegetable/Meat.
        var customSectionName = isNpm && section is null && !string.IsNullOrWhiteSpace(request.CustomSectionName)
            ? request.CustomSectionName.Trim()
            : null;

        // The daily fee this import stamps on the stalls it creates. For one of the three areas it is the office's rate
        // FOR THAT AREA, which is its market rate wherever it prices that area no differently — so Cantilan stamps the
        // same ₱30 it always did. A fee the office has never stated is refused rather than taken as zero.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var rateAsOf = DateOnly.FromDateTime(clock.PhilippineNow);
        var statedDailyRate = section is { } importedArea
            ? NpmDailyFee.ForAreaOrNull(importedArea, rateSnapshot, rateAsOf)
            : rateSnapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, rateAsOf);
        if (statedDailyRate is not { } npmDailyRate)
            return Result<BulkImportResultDto>.Failure(FeeRateMessages.NotStated(FeeRateKey.NpmDailyStall));

        // Load the facility's existing stalls (tracked) so an imported row landing on an EXPIRED/CLOSED
        // stall number renews that stall instead of being rejected, while an ACTIVE stall is protected.
        var existingStalls = await stallRepo.GetStallsWithContractsByFacilityAsync(request.FacilityCode, section, customSectionName, ct);
        var today = clock.PhilippineToday;

        // The daily rate stamped on imported NPM stalls: a custom section uses the rate from the import form
        // (else inherits the section's existing rate, else the ordinance rate); canonical uses the ordinance
        // rate exactly as before.
        //
        // Where the office has STATED a fee for that section, an imported stall is left carrying NO rate of its own, so it
        // follows the section. Stamping a figure here would give every imported stall an own rate, and an own rate
        // outranks its section's for ever: the office would have priced the section and gone on collecting the market's
        // rate from every stall the import created. Worse here than on the form, because one import does it to a whole row
        // of the market at once.
        var sectionStatedRate = customSectionName is null
            ? null
            : rateSnapshot.ResolveSectionOrNull(FacilityCode.NPM, customSectionName, rateAsOf);

        decimal? npmStallDailyRate = customSectionName is not null
            ? (request.CustomDailyRate is { } cr && cr > 0m
                ? cr
                : sectionStatedRate is > 0m
                    ? null
                    : existingStalls.FirstOrDefault()?.DailyRate ?? npmDailyRate)
            : npmDailyRate;

        // Grouped rather than keyed, so a number that two existing spaces share is DETECTED. It used to be a dictionary
        // assignment, where the second space silently replaced the first — so a row would have renewed, reopened or re-rated
        // whichever space the query happened to return last. Such a row is refused below: this import decides which occupancy
        // a lessee is being recorded against, and it must not guess.
        var existingByNo = existingStalls
            .Where(s => !string.IsNullOrWhiteSpace(s.StallNo))
            .GroupBy(s => s.StallNo!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Occupants with a CURRENT active contract — re-importing one would duplicate a live payor, so skip.
        var activeOccupants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in existingStalls.Where(s => IsActivelyOccupied(s, today)))
            foreach (var c in s.Contracts.Where(c => c.IsCollectableOn(today)))
                activeOccupants.Add(NormalizeName(c.ActualOccupant));

        var results = new List<BulkImportRowResult>();
        var newStalls = new List<Stall>();
        var newContracts = new List<Contract>();
        var usedStallNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Stalls this batch handed to a new lessee: their old payor links are revoked with the batch.
        var reletStallIds = new List<Guid>();

        foreach (var row in request.Rows)
        {
            var stallNo = (row.StallNo ?? string.Empty).Trim();
            var occupant = (row.ActualOccupant ?? string.Empty).Trim();

            // The rate the sheet actually states. On a list of spaces let without a contract the "Monthly Rental
            // per Contract" column is empty precisely because there is no contract, and the figure lives in
            // "Actual Mo. Rental" — the office's own barbecue and ice-plant lists are entirely of that shape.
            // Reading the rate from there is what the sheet says; rejecting the row for a column it never fills
            // turned those lists away at the door.
            var monthlyRate = row.MonthlyRate > 0m ? row.MonthlyRate : (row.ActualMonthlyRental ?? 0m);

            // A row that names nobody on a contract and states no term is not a signed contract. Reading it as an
            // open-ended occupancy is the basis a9883f2 added, and it keeps the space out of renewal and expiry
            // work instead of failing it for a duration it never had. A row that DOES name a leasee but omits the
            // term is still an error: that is a missing figure, not a space held without a contract.
            var arrangement = row.Arrangement == OccupancyArrangement.SignedContract
                && string.IsNullOrWhiteSpace(row.NameOnContract)
                && row.ContractYears < 1
                    ? OccupancyArrangement.SpaceOnly
                    : row.Arrangement;

            var error = ValidateRow(stallNo, occupant, row, usedStallNos, monthlyRate, arrangement);
            if (error is not null)
            {
                results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, false, error));
                continue;
            }

            // Never duplicate a live payor — the office renews their existing stall instead.
            if (activeOccupants.Contains(NormalizeName(occupant)))
            {
                results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, false,
                    $"'{occupant}' is already an active stallholder — renew that stall instead of importing."));
                usedStallNos.Add(stallNo);
                continue;
            }

            var fees = ApplicableFees.BaseRental;
            if (isNpm && section == MarketSection.FishSection)
                fees |= ApplicableFees.FishFee; // fish stalls always carry the ₱1/kg fee
            // Utility applicability chosen for this import batch (NPM only) — electricity/water are metered
            // add-ons per stall, so the batch flag stamps them onto every newly created NPM stall.
            if (isNpm && request.ApplyElectricity)
                fees |= ApplicableFees.Electricity;
            if (isNpm && request.ApplyWater)
                fees |= ApplicableFees.Water;
            var areaLocation = ParseNccAreaLocation(request.FacilityCode, row.AreaLocation);
            var effectivity = DateOnly.FromDateTime(row.EffectivityDate ?? clock.PhilippineNow);
            var nameOnContract = string.IsNullOrWhiteSpace(row.NameOnContract) ? null : row.NameOnContract!.Trim();
            var areaSqm = row.AreaSqm.HasValue && row.AreaSqm.Value > 0 ? row.AreaSqm : null;

            if (existingByNo.TryGetValue(stallNo, out var existingMatches))
            {
                // Two existing spaces carry this number, so the row does not say which occupancy it belongs to. Refused on
                // the office's instruction rather than applied to one of them: renewing the wrong space would move a lessee
                // onto an account that is not theirs, and the row would report success.
                if (existingMatches.Count > 1)
                {
                    results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, false,
                        $"{existingMatches.Count} spaces here are numbered {stallNo}, so this row does not say which one " +
                        "this lessee holds. Give the duplicates distinct numbers, then import again."));
                    usedStallNos.Add(stallNo);
                    continue;
                }

                var existing = existingMatches[0];
                // An active contract still occupies this stall — cannot import over it.
                if (IsActivelyOccupied(existing, today))
                {
                    results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, false,
                        $"Stall {stallNo} is occupied by an active contract."));
                    usedStallNos.Add(stallNo);
                    continue;
                }

                // Already imported. The office's lists are expired sheets — a three-year term from 2023 has run out
                // — so nothing above stops the SAME row being taken again on a second upload, and each run added
                // another term to the stall and another month of arrears with it. A stall whose latest term is this
                // very occupancy, from this very date, has already been recorded.
                var latestTerm = existing.Contracts
                    .OrderByDescending(c => c.EffectivityDate)
                    .ThenByDescending(c => c.CreatedAt)
                    .FirstOrDefault();

                if (latestTerm is not null
                    && latestTerm.EffectivityDate == effectivity
                    && NormalizeName(latestTerm.ActualOccupant).Equals(NormalizeName(occupant), StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, false,
                        $"'{occupant}' is already recorded on stall {stallNo} from {effectivity:MMM d, yyyy} — this row was imported before."));
                    usedStallNos.Add(stallNo);
                    continue;
                }

                // Expired or closed → RENEW: end any lapsed term, reopen if closed, refresh rate/area, and
                // start a fresh contract on the SAME stall (its number is reused, no duplicate row).
                // The outgoing occupancy ended the day before the incoming one begins — the same rule the
                // add-vendor path uses — so each lessee's collections and arrears stay on their own account. It
                // can never be dated before that term started (a backdated sheet row).
                foreach (var c in existing.Contracts.Where(c => c.IsActive).ToList())
                {
                    var endedOn = effectivity > c.EffectivityDate ? effectivity.AddDays(-1) : c.EffectivityDate;
                    c.Terminate(Actor, endedOn);
                }
                if (existing.Status == StallStatus.Closed)
                    existing.Reopen(Actor);
                existing.UpdateRates(monthlyRate, isNpm ? npmStallDailyRate : existing.DailyRate, Actor);
                if (areaSqm.HasValue)
                    existing.UpdateAreaInfo(areaSqm, existing.AreaNote, existing.Remarks, Actor);
                // Apply the batch's utility choice to the renewed stall too (additive — never strips fees).
                if (isNpm && (request.ApplyElectricity || request.ApplyWater))
                    existing.AddUtilityFees(request.ApplyElectricity, request.ApplyWater, Actor);

                newContracts.Add(Contract.Create(
                    existing.Id, occupant, nameOnContract, effectivity, row.ContractYears,
                    monthlyRate, row.ActualMonthlyRental, null, Actor, arrangement));

                // The space changed hands: a payor account still linked to it belonged to the previous lessee and
                // must not see or pay the incoming lessee's dues. Collected here, revoked with the batch.
                reletStallIds.Add(existing.Id);

                usedStallNos.Add(stallNo);
                results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, true, null));
                continue;
            }

            // Genuinely new stall number → create a new stall + contract.
            var stall = Stall.Create(
                facility.Id, stallNo, monthlyRate, fees, section, areaLocation, areaSqm, null,
                isNpm ? npmStallDailyRate : null, null, StallType.Permanent, Actor, customSectionName: customSectionName);
            newStalls.Add(stall);
            newContracts.Add(Contract.Create(
                stall.Id, occupant, nameOnContract, effectivity, row.ContractYears,
                monthlyRate, row.ActualMonthlyRental, null, Actor, arrangement));
            usedStallNos.Add(stallNo);
            results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, true, false, null));
        }

        if (newStalls.Count > 0 || newContracts.Count > 0)
        {
            // Register the custom section so it becomes a reusable option (no-op if already present).
            if (customSectionName is not null)
                facility.AddCustomSection(customSectionName, Actor);

            // New stalls + all new contracts (incl. renewals on tracked existing stalls) persist together.
            foreach (var stall in newStalls)
                await stallRepo.AddAsync(stall, ct);
            foreach (var contract in newContracts)
                await stallRepo.AddContractAsync(contract, ct);

            // A re-let space's previous payor links go with the same save, so no window exists in which the
            // departed lessee's login can see the incoming lessee's dues.
            foreach (var stallId in reletStallIds)
                await payorRepo.RemoveStallLinksAsync(stallId, ct);

            await uow.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        }

        var created = results.Count(r => r.Created);
        var renewed = results.Count(r => r.Renewed);
        var failed = results.Count(r => !r.Created && !r.Renewed);
        var dto = new BulkImportResultDto(request.Rows.Count, created, renewed, failed, results);
        return Result<BulkImportResultDto>.Success(dto);
    }

    // Case-insensitive, whitespace-collapsed name key so "Juan  Dela Cruz" and "juan dela cruz" match.
    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

    // A stall is actively occupied when it is Active AND has a contract whose term covers today —
    // the only case an imported row must NOT overwrite.
    private static bool IsActivelyOccupied(Stall s, DateOnly today) =>
        s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsCollectableOn(today));

    private const decimal MaxAmount = 1_000_000m; // sanity cap to catch mis-parsed/garbage numbers
    private const double MaxArea = 100_000d;

    // Mirrors CreateStallCommandValidator's rules, but returns a per-row message instead of failing
    // the whole batch. Lengths match the DB columns (varchar(100)); also enforces in-file uniqueness.
    // <paramref name="monthlyRate"/> and <paramref name="arrangement"/> are the values the row will actually be
    // saved with, so the row is judged on what it states rather than on which column it happens to state it in.
    private static string? ValidateRow(
        string stallNo,
        string occupant,
        ImportStallRow row,
        HashSet<string> usedStallNos,
        decimal monthlyRate,
        OccupancyArrangement arrangement)
    {
        if (string.IsNullOrWhiteSpace(occupant))
            return "Actual occupant is required.";
        if (IsPlaceholderOccupant(occupant))
            return "Closed/vacant rows cannot be imported as active stallholders. Remove this row or manage it through the closed/inactive stall account flow.";
        if (occupant.Length > 100)
            return "Actual occupant name cannot exceed 100 characters.";

        if (!string.IsNullOrWhiteSpace(row.NameOnContract) && row.NameOnContract!.Trim().Length > 100)
            return "Name on contract cannot exceed 100 characters.";

        if (string.IsNullOrWhiteSpace(stallNo))
            return "Stall number is required.";
        if (stallNo.Length > 20)
            return "Stall number cannot exceed 20 characters.";

        if (monthlyRate <= 0)
            return "Monthly rate must be greater than ₱0 — fill either the contract rental or the actual monthly rental.";
        if (monthlyRate > MaxAmount)
            return "Monthly rate is unreasonably large — please check the value.";

        if (row.ActualMonthlyRental is < 0m)
            return "Actual monthly rental cannot be negative.";
        if (row.ActualMonthlyRental > MaxAmount)
            return "Actual monthly rental is unreasonably large — please check the value.";

        if (row.AreaSqm is < 0d)
            return "Area (sqm) cannot be negative.";
        if (row.AreaSqm > MaxArea)
            return "Area (sqm) is unreasonably large — please check the value.";

        // Only a signed contract has a term to check. A row the office marked "No contract" is open-ended, so
        // demanding a number of years would reject exactly the rows a barbecue or ice-plant list is made of.
        if (arrangement == OccupancyArrangement.SignedContract
            && (row.ContractYears < 1 || row.ContractYears > 10))
        {
            return "Contract duration must be between 1 and 10 years.";
        }

        if (usedStallNos.Contains(stallNo))
            return "Duplicate stall number in this file.";

        // Existing stall numbers are NOT rejected here anymore — the handler decides create vs. renew
        // (an expired/closed stall's number is reused via renewal; an active one is protected).
        return null;
    }

    // Placeholder / non-stallholder occupant markers copied straight from source report exports
    // (e.g. an NPM printout marks a vacated stall's occupant as "Closed"). Importing these as active
    // stalls would inflate active-stall counts and surface them as unpaid/delinquent — corrupting
    // financial reporting. Rejected in the handler (not only the UI) so direct API calls are protected
    // too. A stall that is genuinely closed is managed through the closed/inactive stall flow.
    private static readonly HashSet<string> PlaceholderOccupants = new(StringComparer.Ordinal)
    {
        "closed", "close", "vacant", "vacated", "n/a", "na", "none", "nil", "-", "--", "---",
    };

    // Normalises the occupant (case-insensitive, all whitespace removed) so "N / A", " Closed " and
    // "CLOSED" all match the placeholder set.
    private static bool IsPlaceholderOccupant(string occupant)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(occupant, @"\s+", "").ToLowerInvariant();
        return normalized.Length > 0 && PlaceholderOccupants.Contains(normalized);
    }

    // NCC area location is parsed explicitly: a recognised value maps to its enum; any other non-empty
    // value (typo / unsupported label) falls back to Standard rather than silently becoming Extension.
    // Blank means "no specific location" (null).
    private static NccAreaLocation? ParseNccAreaLocation(FacilityCode code, string? raw)
    {
        if (code != FacilityCode.NCC || string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value.Equals("Extension", StringComparison.OrdinalIgnoreCase)) return NccAreaLocation.Extension;
        if (value.Equals("Corner", StringComparison.OrdinalIgnoreCase)) return NccAreaLocation.Corner;
        return NccAreaLocation.Standard;
    }
}

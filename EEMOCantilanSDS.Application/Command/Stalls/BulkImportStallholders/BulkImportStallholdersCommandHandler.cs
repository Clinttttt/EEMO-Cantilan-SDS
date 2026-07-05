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
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext) : IRequestHandler<BulkImportStallholdersCommand, Result<BulkImportResultDto>>
{
    private const string Actor = "Admin"; // matches CreateStallCommandHandler (no per-request user attribution)

    public async Task<Result<BulkImportResultDto>> Handle(BulkImportStallholdersCommand request, CancellationToken ct)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, ct);
        if (facility is null)
            return Result<BulkImportResultDto>.NotFound();

        var isNpm = request.FacilityCode == FacilityCode.NPM;
        var section = isNpm ? request.Section : null;

        // Resolve the current municipality's NPM daily fee (falls back to the ordinance constant, so
        // Cantilan seeds the same ₱30 DailyRate). Imported NPM stalls are stamped with this rate.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var npmDailyRate = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, DateOnly.FromDateTime(PhilippineTime.Now));

        // Load the facility's existing stalls (tracked) so an imported row landing on an EXPIRED/CLOSED
        // stall number renews that stall instead of being rejected, while an ACTIVE stall is protected.
        var existingStalls = await stallRepo.GetStallsWithContractsByFacilityAsync(request.FacilityCode, section, ct);
        var today = PhilippineTime.Today;

        var existingByNo = new Dictionary<string, Stall>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in existingStalls)
            existingByNo[s.StallNo] = s;

        // Occupants with a CURRENT active contract — re-importing one would duplicate a live payor, so skip.
        var activeOccupants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in existingStalls.Where(s => IsActivelyOccupied(s, today)))
            foreach (var c in s.Contracts.Where(c => c.IsCollectableOn(today)))
                activeOccupants.Add(NormalizeName(c.ActualOccupant));

        var results = new List<BulkImportRowResult>();
        var newStalls = new List<Stall>();
        var newContracts = new List<Contract>();
        var usedStallNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in request.Rows)
        {
            var stallNo = (row.StallNo ?? string.Empty).Trim();
            var occupant = (row.ActualOccupant ?? string.Empty).Trim();

            var error = ValidateRow(stallNo, occupant, row, usedStallNos);
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
            var areaLocation = ParseNccAreaLocation(request.FacilityCode, row.AreaLocation);
            var effectivity = DateOnly.FromDateTime(row.EffectivityDate ?? PhilippineTime.Now);
            var nameOnContract = string.IsNullOrWhiteSpace(row.NameOnContract) ? null : row.NameOnContract!.Trim();
            var areaSqm = row.AreaSqm.HasValue && row.AreaSqm.Value > 0 ? row.AreaSqm : null;

            if (existingByNo.TryGetValue(stallNo, out var existing))
            {
                // An active contract still occupies this stall — cannot import over it.
                if (IsActivelyOccupied(existing, today))
                {
                    results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, false,
                        $"Stall {stallNo} is occupied by an active contract."));
                    usedStallNos.Add(stallNo);
                    continue;
                }

                // Expired or closed → RENEW: end any lapsed term, reopen if closed, refresh rate/area, and
                // start a fresh contract on the SAME stall (its number is reused, no duplicate row).
                foreach (var c in existing.Contracts.Where(c => c.IsActive).ToList())
                    c.Terminate(Actor);
                if (existing.Status == StallStatus.Closed)
                    existing.Reopen(Actor);
                existing.UpdateRates(row.MonthlyRate, isNpm ? npmDailyRate : existing.DailyRate, Actor);
                if (areaSqm.HasValue)
                    existing.UpdateAreaInfo(areaSqm, existing.AreaNote, existing.Remarks, Actor);

                newContracts.Add(Contract.Create(
                    existing.Id, occupant, nameOnContract, effectivity, row.ContractYears,
                    row.MonthlyRate, row.ActualMonthlyRental, null, Actor));

                usedStallNos.Add(stallNo);
                results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, true, null));
                continue;
            }

            // Genuinely new stall number → create a new stall + contract.
            var stall = Stall.Create(
                facility.Id, stallNo, row.MonthlyRate, fees, section, areaLocation, areaSqm, null,
                isNpm ? npmDailyRate : null, null, StallType.Permanent, Actor);
            newStalls.Add(stall);
            newContracts.Add(Contract.Create(
                stall.Id, occupant, nameOnContract, effectivity, row.ContractYears,
                row.MonthlyRate, row.ActualMonthlyRental, null, Actor));
            usedStallNos.Add(stallNo);
            results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, true, false, null));
        }

        if (newStalls.Count > 0 || newContracts.Count > 0)
        {
            // New stalls + all new contracts (incl. renewals on tracked existing stalls) persist together.
            foreach (var stall in newStalls)
                await stallRepo.AddAsync(stall, ct);
            foreach (var contract in newContracts)
                await stallRepo.AddContractAsync(contract, ct);

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
    private static string? ValidateRow(
        string stallNo,
        string occupant,
        ImportStallRow row,
        HashSet<string> usedStallNos)
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

        if (row.MonthlyRate <= 0)
            return "Monthly rate must be greater than ₱0.";
        if (row.MonthlyRate > MaxAmount)
            return "Monthly rate is unreasonably large — please check the value.";

        if (row.ActualMonthlyRental is < 0m)
            return "Actual monthly rental cannot be negative.";
        if (row.ActualMonthlyRental > MaxAmount)
            return "Actual monthly rental is unreasonably large — please check the value.";

        if (row.AreaSqm is < 0d)
            return "Area (sqm) cannot be negative.";
        if (row.AreaSqm > MaxArea)
            return "Area (sqm) is unreasonably large — please check the value.";

        if (row.ContractYears < 1 || row.ContractYears > 10)
            return "Contract duration must be between 1 and 10 years.";

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

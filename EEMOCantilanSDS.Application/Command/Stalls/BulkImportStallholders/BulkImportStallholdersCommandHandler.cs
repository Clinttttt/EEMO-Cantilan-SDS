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

        var results = new List<BulkImportRowResult>();
        var stallsToAdd = new List<Stall>();
        var contractsToAdd = new List<Contract>();
        // Tracks stall numbers used within this batch (case-insensitive) to catch in-file duplicates,
        // which the per-row DB uniqueness check cannot see (nothing is saved until the end).
        var usedStallNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in request.Rows)
        {
            var stallNo = (row.StallNo ?? string.Empty).Trim();
            var occupant = (row.ActualOccupant ?? string.Empty).Trim();

            var error = await ValidateRowAsync(request.FacilityCode, section, stallNo, occupant, row, usedStallNos, ct);
            if (error is not null)
            {
                results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, false, error));
                continue;
            }

            var fees = ApplicableFees.BaseRental;
            if (isNpm && section == MarketSection.FishSection)
                fees |= ApplicableFees.FishFee; // fish stalls always carry the ₱1/kg fee

            NccAreaLocation? areaLocation = ParseNccAreaLocation(request.FacilityCode, row.AreaLocation);

            var stall = Stall.Create(
                facility.Id,
                stallNo,
                row.MonthlyRate,
                fees,
                section,
                areaLocation,
                row.AreaSqm.HasValue && row.AreaSqm.Value > 0 ? row.AreaSqm : null,
                null,
                isNpm ? npmDailyRate : null,
                null,
                StallType.Permanent,
                Actor);

            var contract = Contract.Create(
                stall.Id,
                occupant,
                string.IsNullOrWhiteSpace(row.NameOnContract) ? null : row.NameOnContract!.Trim(),
                DateOnly.FromDateTime(row.EffectivityDate ?? PhilippineTime.Now),
                row.ContractYears,
                row.MonthlyRate,
                row.ActualMonthlyRental,
                null,
                Actor);

            stallsToAdd.Add(stall);
            contractsToAdd.Add(contract);
            usedStallNos.Add(stallNo);
            results.Add(new BulkImportRowResult(row.RowNumber, stallNo, occupant, true, null));
        }

        if (stallsToAdd.Count > 0)
        {
            // IDs are assigned at creation, so stalls and their contracts persist in one transaction.
            foreach (var stall in stallsToAdd)
                await stallRepo.AddAsync(stall, ct);
            foreach (var contract in contractsToAdd)
                await stallRepo.AddContractAsync(contract, ct);

            await uow.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        }

        var created = results.Count(r => r.Created);
        var dto = new BulkImportResultDto(request.Rows.Count, created, results.Count - created, results);
        return Result<BulkImportResultDto>.Success(dto);
    }

    private const decimal MaxAmount = 1_000_000m; // sanity cap to catch mis-parsed/garbage numbers
    private const double MaxArea = 100_000d;

    // Mirrors CreateStallCommandValidator's rules, but returns a per-row message instead of failing
    // the whole batch. Lengths match the DB columns (varchar(100)); also enforces in-file uniqueness.
    private async Task<string?> ValidateRowAsync(
        FacilityCode facilityCode,
        MarketSection? section,
        string stallNo,
        string occupant,
        ImportStallRow row,
        HashSet<string> usedStallNos,
        CancellationToken ct)
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

        var unique = await stallRepo.IsStallNoUniqueAsync(facilityCode, section, stallNo, ct);
        if (!unique)
            return "Stall number already exists in this facility.";

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

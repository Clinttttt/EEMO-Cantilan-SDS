using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmUtility;

public class GetMobileNpmUtilityQueryHandler(
    IUtilityBillRepository utilityRepository,
    IStallRegisterQueries stallRegister,
    IFacilityRepository facilityRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser)
    : IRequestHandler<GetMobileNpmUtilityQuery, Result<MobileNpmUtilityDto>>
{
    public async Task<Result<MobileNpmUtilityDto>> Handle(GetMobileNpmUtilityQuery request, CancellationToken ct)
    {
        // Authorization: a collector may only READ NPM data for a facility they are assigned to — mirrors
        // the write path (RecordUtilityPaymentCommandHandler). Admins/heads are unrestricted. The global
        // tenant filter already blocks cross-LGU; this closes the same-LGU cross-facility read gap.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<MobileNpmUtilityDto>.Forbidden();

            var collector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (collector is null || !collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM))
                return Result<MobileNpmUtilityDto>.Forbidden();
        }

        // The month's bills AND anything still owed from earlier months. A utility bill the office raised in
        // June does not stop being collectible in August, and the collector is the one standing at the stall.
        var bills = await utilityRepository.GetForMonthWithOutstandingAsync(request.Year, request.Month, ct);
        var stalls = await stallRegister.GetStallsByFacilityAsync(FacilityCode.NPM, null, ct);
        var byStall = stalls.ToDictionary(s => s.Id);

        // Tenant's own market-section labels (e.g. "Gulayan"), resolved once; falls back to the canonical name.
        var npm = await facilityRepository.GetByCodeAsync(FacilityCode.NPM, ct);

        var rows = bills
            .Select(b =>
            {
                byStall.TryGetValue(b.StallId, out var s);
                var occupant = string.IsNullOrWhiteSpace(s?.ActualOccupant) ? "—" : s!.ActualOccupant!;
                var section = SectionLabel(npm, s?.Section);
                if (string.IsNullOrWhiteSpace(section)) section = s?.CustomSectionName ?? string.Empty;
                return new MobileUtilityBillDto(
                    b.Id, s?.StallNo ?? "—", occupant, section,
                    b.ElecCharge, b.ElecStatus.ToString(), b.ElecBalanceDue,
                    b.WaterCharge, b.WaterStatus.ToString(), b.WaterBalanceDue,
                    b.TotalCharge, b.AmountPaid, b.BalanceDue, b.ElecORNumber, b.WaterORNumber,
                    b.BillingYear, b.BillingMonth, PeriodLabel(b.BillingYear, b.BillingMonth));
            })
            // What still needs collecting first, oldest owed month first within that, so the field app settles
            // the longest-standing bill before this month's.
            .OrderByDescending(r => r.BalanceDue > 0)
            .ThenBy(r => r.BillingYear).ThenBy(r => r.BillingMonth)
            .ThenBy(r => r.StallNo)
            .ToList();

        return Result<MobileNpmUtilityDto>.Success(new MobileNpmUtilityDto(request.Year, request.Month, rows));
    }

    private static string PeriodLabel(int year, int month) =>
        month is >= 1 and <= 12
            ? new DateTime(year, month, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : $"{year:0000}-{month:00}";

    // Tenant label if configured, else the canonical section name (the MarketSection enum stays the key).
    private static string SectionLabel(Facility? facility, MarketSection? section)
    {
        if (section is not { } s)
            return "";
        var custom = facility?.SectionLabel(s);
        if (!string.IsNullOrWhiteSpace(custom))
            return custom!;
        return s switch
        {
            MarketSection.VegetableArea => "Vegetable Area",
            MarketSection.FishSection => "Fish Area",
            MarketSection.MeatSection => "Meat Area",
            _ => ""
        };
    }
}

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
    IStallRepository stallRepository,
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

        var bills = await utilityRepository.GetForMonthAsync(request.Year, request.Month, ct);
        var stalls = await stallRepository.GetStallsByFacilityAsync(FacilityCode.NPM, null, ct);
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
                    b.TotalCharge, b.AmountPaid, b.BalanceDue, b.ElecORNumber, b.WaterORNumber);
            })
            // Show the ones that still need collecting first, then the rest, newest stall grouping aside.
            .OrderByDescending(r => r.BalanceDue > 0)
            .ThenBy(r => r.StallNo)
            .ToList();

        return Result<MobileNpmUtilityDto>.Success(new MobileNpmUtilityDto(request.Year, request.Month, rows));
    }

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

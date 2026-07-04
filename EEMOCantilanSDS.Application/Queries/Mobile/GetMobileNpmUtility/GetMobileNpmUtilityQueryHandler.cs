using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmUtility;

public class GetMobileNpmUtilityQueryHandler(
    IUtilityBillRepository utilityRepository,
    IStallRepository stallRepository)
    : IRequestHandler<GetMobileNpmUtilityQuery, Result<MobileNpmUtilityDto>>
{
    public async Task<Result<MobileNpmUtilityDto>> Handle(GetMobileNpmUtilityQuery request, CancellationToken ct)
    {
        var bills = await utilityRepository.GetForMonthAsync(request.Year, request.Month, ct);
        var stalls = await stallRepository.GetStallsByFacilityAsync(FacilityCode.NPM, null, ct);
        var byStall = stalls.ToDictionary(s => s.Id);

        var rows = bills
            .Select(b =>
            {
                byStall.TryGetValue(b.StallId, out var s);
                var occupant = string.IsNullOrWhiteSpace(s?.ActualOccupant) ? "—" : s!.ActualOccupant!;
                var section = SectionLabel(s?.Section);
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

    private static string SectionLabel(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Area",
        MarketSection.MeatSection => "Meat Area",
        _ => ""
    };
}

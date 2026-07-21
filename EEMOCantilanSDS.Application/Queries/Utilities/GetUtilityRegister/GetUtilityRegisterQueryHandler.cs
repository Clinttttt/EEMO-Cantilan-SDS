using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityRegister;

public class GetUtilityRegisterQueryHandler(
    IStallRepository stallRepository,
    IUtilityBillRepository utilityRepository)
    : IRequestHandler<GetUtilityRegisterQuery, Result<UtilityRegisterDto>>
{
    public async Task<Result<UtilityRegisterDto>> Handle(GetUtilityRegisterQuery request, CancellationToken ct)
    {
        var stalls = await stallRepository.GetStallsByFacilityAsync(FacilityCode.NPM, request.Section, ct);

        // Only current payors: active stalls, a real occupant (not a closed/vacant placeholder), and a
        // contract that has not expired.
        var now = EEMOCantilanSDS.Domain.Common.PhilippineTime.Now.Date;
        var active = stalls.Where(s => s.Status == StallStatus.Active
                && !IsPlaceholderOccupant(s.ActualOccupant)
                && !(s.ContractDate is { } cd && s.ContractYears > 0 && cd.AddYears(s.ContractYears).Date < now))
            .ToList();

        var bills = await utilityRepository.GetForMonthAsync(request.Year, request.Month, ct);
        var byStall = bills.ToDictionary(b => b.StallId);

        var rows = new List<UtilityRegisterRowDto>(active.Count);
        decimal totalDue = 0m, totalPaid = 0m, totalUnpaid = 0m;
        int paid = 0, partial = 0, unpaid = 0, unbilled = 0;

        foreach (var s in active)
        {
            var occupant = string.IsNullOrWhiteSpace(s.ActualOccupant) ? "—" : s.ActualOccupant!;
            var section = SectionLabel(s.Section);
            if (string.IsNullOrWhiteSpace(section)) section = s.CustomSectionName ?? string.Empty;

            if (byStall.TryGetValue(s.Id, out var b))
            {
                totalDue += b.TotalCharge;
                totalPaid += b.AmountPaid;
                totalUnpaid += b.BalanceDue;
                switch (b.Status)
                {
                    case PaymentStatus.Paid: paid++; break;
                    case PaymentStatus.Partial: partial++; break;
                    default: unpaid++; break;
                }

                rows.Add(new UtilityRegisterRowDto(
                    s.Id, s.StallNo, occupant, section,
                    b.Id, true,
                    b.ElecPreviousReading, b.ElecCurrentReading, b.ElecConsumption, b.ElecCharge,
                    b.WaterPreviousReading, b.WaterCurrentReading, b.WaterConsumption, b.WaterCharge,
                    b.TotalCharge, b.Status.ToString(), b.BalanceDue,
                    b.ElecStatus.ToString(), b.WaterStatus.ToString()));
            }
            else
            {
                unbilled++;
                rows.Add(new UtilityRegisterRowDto(
                    s.Id, s.StallNo, occupant, section,
                    null, false,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, "Unbilled", 0,
                    "Unbilled", "Unbilled"));
            }
        }

        var dto = new UtilityRegisterDto(
            request.Year, request.Month,
            totalDue, totalUnpaid, totalPaid,
            paid, partial, unpaid, unbilled,
            rows);

        return Result<UtilityRegisterDto>.Success(dto);
    }

    private static readonly System.Collections.Generic.HashSet<string> PlaceholderOccupants =
        new(StringComparer.OrdinalIgnoreCase) { "closed", "close", "vacant", "vacated", "n/a", "na", "none", "nil", "-", "--", "---" };

    private static bool IsPlaceholderOccupant(string? occupant)
    {
        if (string.IsNullOrWhiteSpace(occupant)) return true;
        var normalized = System.Text.RegularExpressions.Regex.Replace(occupant, @"\s+", "").ToLowerInvariant();
        return PlaceholderOccupants.Contains(normalized);
    }

    private static string SectionLabel(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Area",
        MarketSection.MeatSection => "Meat Area",
        _ => ""
    };
}

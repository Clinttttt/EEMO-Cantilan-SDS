using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityRegister;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Utilities;

/// <summary>
/// What a Statement of Account for utility charges is allowed to say.
///
/// <para>
/// The statement is handed to the stallholder, so every figure on it has to be one the office can stand behind and the
/// payor can check: previous reading, current reading, consumption, THE RATE, and the resulting charge. The register is
/// where all of that comes from — the statement performs no arithmetic of its own — so these tests hold the register to
/// carrying it.
/// </para>
///
/// <para>
/// The rates were missing from the register until this was built. Consumption and charge were there, so a statement
/// could have shown an amount while leaving the payor no way to verify it; deriving the rate by dividing charge by
/// consumption would have been a fabrication the moment consumption was nought.
/// </para>
/// </summary>
public class UtilityStatementFiguresTests
{
    private static StallDto Stall(string stallNo, bool electricity = true, bool water = true) => new(
        Guid.NewGuid(), stallNo, StallStatus.Active,
        ActualOccupant: $"Payor {stallNo}", NameOnContract: $"Payor {stallNo}",
        AreaSqm: 4.8, ContractDate: DateTime.Today.AddMonths(-2),
        MonthlyRate: 900m, DailyRate: 30m, ORNumber: null,
        Section: MarketSection.VegetableArea, AreaLocation: null, AreaNote: null, Remarks: null,
        ContractYears: 3, CustomSectionName: null,
        HasElectricity: electricity, HasWater: water);

    private static GetUtilityRegisterQueryHandler Handler(IReadOnlyList<StallDto> stalls, IReadOnlyList<UtilityBill> bills)
    {
        var stallRepo = new Mock<IStallRegisterQueries>();
        stallRepo.Setup(r => r.GetStallsByFacilityAsync(FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stalls.ToList());

        var utilityRepo = new Mock<IUtilityBillRepository>();
        utilityRepo.Setup(r => r.GetForMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bills.ToList());

        return new GetUtilityRegisterQueryHandler(stallRepo.Object, utilityRepo.Object, new FixedClock(DateTime.UtcNow));
    }

    [Fact]
    public async Task TheRegisterCarriesTheRATESAStatementMustShow()
    {
        // 120 kWh at ₱11.50 and 8 cu.m at ₱25.00 — figures a payor can multiply out on the sheet.
        var stall = Stall("14");
        var bill = UtilityBill.Create(stall.Id, 2026, 8,
            elecPreviousReading: 1_000m, elecCurrentReading: 1_120m, elecRatePerKwh: 11.50m,
            waterPreviousReading: 40m, waterCurrentReading: 48m, waterRatePerCubicMeter: 25m);

        var result = await Handler([stall], [bill]).Handle(new GetUtilityRegisterQuery(2026, 8, null), CancellationToken.None);

        var row = Assert.Single(result.Value!.Rows);

        Assert.Equal(11.50m, row.ElecRatePerKwh);
        Assert.Equal(25m, row.WaterRatePerCubicMeter);

        // And the arithmetic the statement prints must close, line by line.
        Assert.Equal(120m, row.ElecConsumption);
        Assert.Equal(row.ElecConsumption * row.ElecRatePerKwh, row.ElecCharge);
        Assert.Equal(8m, row.WaterConsumption);
        Assert.Equal(row.WaterConsumption * row.WaterRatePerCubicMeter, row.WaterCharge);
        Assert.Equal(row.ElecCharge + row.WaterCharge, row.TotalCharge);
    }

    [Fact]
    public async Task ThePAYMENTSRECEIVEDLineReconcilesWithTheBill()
    {
        // The statement prints "Less payments received" as TotalCharge - BalanceDue rather than carrying a separate
        // paid figure, so that it can never contradict the receipt the office already issued. This holds that identity.
        var stall = Stall("14");
        var bill = UtilityBill.Create(stall.Id, 2026, 8,
            elecPreviousReading: 0m, elecCurrentReading: 100m, elecRatePerKwh: 10m,      // ₱1,000
            waterPreviousReading: 0m, waterCurrentReading: 0m, waterRatePerCubicMeter: 25m);

        bill.RecordPayment(
            elecOrNumber: "OR-9001", waterOrNumber: null, collectorId: null,
            elecStatus: PaymentStatus.Partial, elecPartialAmount: 400m,
            waterStatus: PaymentStatus.Unpaid, waterPartialAmount: null);

        var result = await Handler([stall], [bill]).Handle(new GetUtilityRegisterQuery(2026, 8, null), CancellationToken.None);
        var row = Assert.Single(result.Value!.Rows);

        Assert.Equal(1_000m, row.TotalCharge);
        Assert.Equal(600m, row.BalanceDue);
        Assert.Equal(bill.AmountPaid, row.TotalCharge - row.BalanceDue);   // what the statement prints
        Assert.Equal(400m, row.TotalCharge - row.BalanceDue);
    }

    [Fact]
    public async Task AStallWithNOBILLCarriesNoRatesAndCannotBeStated()
    {
        // The rule the statement view enforces: a payor with no reading recorded is left out. If such a row were
        // issued a statement it would state an amount due of nought over the office's letterhead — a false statement,
        // and one a payor could reasonably hold up against a later bill.
        var stall = Stall("15");

        var result = await Handler([stall], []).Handle(new GetUtilityRegisterQuery(2026, 8, null), CancellationToken.None);
        var row = Assert.Single(result.Value!.Rows);

        Assert.False(row.HasBill);
        Assert.Equal("Unbilled", row.Status);
        Assert.Equal(0m, row.ElecRatePerKwh);
        Assert.Equal(0m, row.WaterRatePerCubicMeter);
        Assert.Equal(0m, row.TotalCharge);
    }

    [Fact]
    public async Task ARateThatCHANGEDLaterDoesNotRewriteAnOlderStatement()
    {
        // The rate is taken from the BILL, not from the facility's current rate, so reissuing a statement for a past
        // period states what was actually charged. A statement that quietly re-prices an old period would contradict
        // the receipt the payor is holding.
        var stall = Stall("14", electricity: true, water: false);
        var billedAtOldRate = UtilityBill.Create(stall.Id, 2026, 8,
            elecPreviousReading: 0m, elecCurrentReading: 50m, elecRatePerKwh: 9m,
            waterPreviousReading: 0m, waterCurrentReading: 0m, waterRatePerCubicMeter: 0m);

        var result = await Handler([stall], [billedAtOldRate]).Handle(new GetUtilityRegisterQuery(2026, 8, null), CancellationToken.None);
        var row = Assert.Single(result.Value!.Rows);

        Assert.Equal(9m, row.ElecRatePerKwh);
        Assert.Equal(450m, row.ElecCharge);
    }
}

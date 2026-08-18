using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using NpmReports = EEMOCantilanSDS.Client.Components.Pages.Reports.NpmReports;

/// <summary>
/// The Statement of Account for utility charges, rendered.
///
/// <para>
/// This sheet is handed to a stallholder, so the figures on it are asserted against a register the test controls: the
/// readings, the consumption, the RATE, the charge, what has been paid and what is still due. The statement performs no
/// arithmetic of its own — it prints what the register carries — and these tests hold it to that.
/// </para>
///
/// <para>
/// The rule worth having a test for is the exclusion. A stall with no reading recorded has no charge, and issuing it a
/// statement would put "amount due ₱0.00" over the office's letterhead for a payor who may well owe something once the
/// meter is read. Those payors are left out and counted on screen instead.
/// </para>
/// </summary>
public class UtilityStatementViewTests : TestContext
{
    private const string BilledPayor = "Merlita A. Abuso";
    private const string UnbilledPayor = "Diego Brando";

    private static UtilityRegisterRowDto Billed() => new(
        StallId: Guid.NewGuid(), StallNo: "14", Occupant: BilledPayor, Section: "Vegetable Area",
        BillId: Guid.NewGuid(), HasBill: true,
        ElecPreviousReading: 1_000m, ElecCurrentReading: 1_120m, ElecConsumption: 120m, ElecCharge: 1_380m,
        WaterPreviousReading: 40m, WaterCurrentReading: 48m, WaterConsumption: 8m, WaterCharge: 200m,
        TotalCharge: 1_580m, Status: "Partial", BalanceDue: 580m,
        ElecStatus: "Paid", WaterStatus: "Unpaid",
        HasElectricity: true, HasWater: true,
        ElecRatePerKwh: 11.50m, WaterRatePerCubicMeter: 25m);

    private static UtilityRegisterRowDto Unbilled() => new(
        StallId: Guid.NewGuid(), StallNo: "15", Occupant: UnbilledPayor, Section: "Vegetable Area",
        BillId: null, HasBill: false,
        ElecPreviousReading: 0m, ElecCurrentReading: 0m, ElecConsumption: 0m, ElecCharge: 0m,
        WaterPreviousReading: 0m, WaterCurrentReading: 0m, WaterConsumption: 0m, WaterCharge: 0m,
        TotalCharge: 0m, Status: "Unbilled", BalanceDue: 0m,
        ElecStatus: "Unbilled", WaterStatus: "Unbilled",
        HasElectricity: true, HasWater: true);

    private IRenderedComponent<NpmReports> RenderStatements(params UtilityRegisterRowDto[] rows)
    {
        this.AddTestAuthorization().SetAuthorized("cly.sullano").SetRoles("SuperAdmin");

        var register = new UtilityRegisterDto(
            2026, 8,
            TotalDue: rows.Sum(r => r.TotalCharge),
            TotalUnpaid: rows.Sum(r => r.BalanceDue),
            TotalPaid: rows.Sum(r => r.TotalCharge - r.BalanceDue),
            PaidCount: rows.Count(r => r.Status == "Paid"),
            PartialCount: rows.Count(r => r.Status == "Partial"),
            UnpaidCount: rows.Count(r => r.Status == "Unpaid"),
            UnbilledCount: rows.Count(r => !r.HasBill),
            Rows: rows);

        var utilities = new Mock<IUtilitiesApiClient>();
        utilities.Setup(u => u.GetRegisterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MarketSection?>()))
            .ReturnsAsync(Result<UtilityRegisterDto>.Success(register));

        Services.AddSingleton(utilities.Object);
        // The collection report is not what a statement is drawn from, so it is left unavailable rather than
        // fabricated: a failed result is a state the page already handles, and inventing a report here could only
        // disguise a statement reading the wrong source.
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(f => f.GetFacilityReportsAsync(
                It.IsAny<FacilityCode>(), It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Failure("not needed for a statement"));
        Services.AddSingleton(facilities.Object);

        // The page loads the NPM rates on initialise, so a bare mock returning null would fail before any markup
        // exists. The figures are irrelevant here - the statement's amounts come from the register above.
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetNpmRatesAsync())
            .ReturnsAsync(Result<NpmRatesDto>.Success(new NpmRatesDto(DailyRate: 30m, FishRate: 1m)));
        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Securities.UiLoadingService>();

        var page = RenderComponent<NpmReports>();

        // Into the utility view, then produce the statements — the same two clicks the office makes.
        page.FindAll("button").First(b => b.TextContent.Contains("Utility", StringComparison.OrdinalIgnoreCase)).Click();
        page.WaitForState(() => page.Markup.Contains("Generate Billing Statement"), TimeSpan.FromSeconds(5));
        page.FindAll("button").First(b => b.TextContent.Contains("Generate Billing Statement")).Click();

        return page;
    }

    [Fact]
    public void ItStatesTheReadingsTheRateAndTheAmountDue()
    {
        var page = RenderStatements(Billed());
        var markup = page.Markup;

        Assert.Contains("Statement of Account", markup);
        Assert.Contains(BilledPayor, markup);

        // Electricity: 1,000 → 1,120 = 120 kWh at ₱11.50 = ₱1,380.00
        Assert.Contains("1,000.00", markup);
        Assert.Contains("1,120.00", markup);
        Assert.Contains("120.00 kWh", markup);
        Assert.Contains("11.50", markup);
        Assert.Contains("1,380.00", markup);

        // Water: 40 → 48 = 8 cu.m at ₱25.00 = ₱200.00
        Assert.Contains("8.00 cu.m", markup);
        Assert.Contains("200.00", markup);

        // Total ₱1,580.00, paid ₱1,000.00 (total less balance), due ₱580.00.
        Assert.Contains("1,580.00", markup);
        Assert.Contains("1,000.00", markup);
        Assert.Contains("580.00", markup);
    }

    [Fact]
    public void APayorWithNOREADINGIsLeftOutAndCounted()
    {
        var page = RenderStatements(Billed(), Unbilled());
        var markup = page.Markup;

        Assert.Contains(BilledPayor, markup);
        Assert.DoesNotContain(UnbilledPayor, markup);       // no statement may be issued for them
        Assert.Contains("no reading recorded", markup);      // and the office is told why
        Assert.Single(page.FindAll(".statement-sheet"));
    }

    [Fact]
    public void WithNothingBilledItSaysSoRatherThanPrintingAnEmptySheet()
    {
        var page = RenderStatements(Unbilled());

        Assert.Empty(page.FindAll(".statement-sheet"));
        Assert.Contains("A statement can only be issued once a reading is entered", page.Markup);
    }

    [Fact]
    public void TheSheetCarriesTheOfficesOwnLetterheadAndSignatories()
    {
        // An official document, so it is headed and signed like the office's other sheets rather than being a bare table.
        var markup = RenderStatements(Billed()).Markup;

        Assert.Contains("Republic of the Philippines", markup);
        Assert.Contains("print-report-signatures", markup);
    }
}

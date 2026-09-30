using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// IA-050: ECF and WCF are direct approved amounts, not meter x rate. The amount is stored as one unit so every existing
/// charge, balance and report stays exact, and the basis records that the readings are not evidence.
/// </summary>
public class UtilityDirectApprovedBasisTests
{
    private static UtilityBill Direct(decimal ecf, decimal wcf)
    {
        var (ep, ec, er) = UtilityBill.DirectApprovedReadings(ecf);
        var (wp, wc, wr) = UtilityBill.DirectApprovedReadings(wcf);
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 9, ep, ec, er, wp, wc, wr);
        bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.DirectApproved);
        return bill;
    }

    [Fact]
    public void ADirectApprovedAmount_IsTheCharge_WithNoMeterMultiplication()
    {
        var bill = Direct(650m, 10m);

        Assert.Equal(650m, bill.ElecCharge);
        Assert.Equal(10m, bill.WaterCharge);
        Assert.Equal(660m, bill.TotalCharge);
        Assert.Equal(UtilityCalculationBasis.DirectApproved, bill.ElecCalculationBasis);
    }

    [Fact]
    public void ANewBill_IsMeteredByDefault_SoHistoricalRowsKeepTheirMeaning()
    {
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 8, 100m, 150m, 12m, 10m, 14m, 25m);

        Assert.Equal(UtilityCalculationBasis.Metered, bill.ElecCalculationBasis);
        Assert.Equal(UtilityCalculationBasis.Metered, bill.WaterCalculationBasis);
        Assert.Equal(600m, bill.ElecCharge);
    }

    [Fact]
    public void ChangingTheBasis_BumpsTheSourceVersion_SoAStaleQuoteIsRefused()
    {
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 9, 0m, 0m, 0m, 0m, 0m, 0m);
        var before = bill.WaterSourceVersion;

        bill.SetCalculationBasis(UtilityCalculationBasis.Metered, UtilityCalculationBasis.DirectApproved);

        Assert.Equal(before + 1, bill.WaterSourceVersion);
        Assert.Equal(1L, bill.ElectricitySourceVersion);
    }

    [Fact]
    public void ASettledPart_CannotChangeHowItWasAssessed()
    {
        var bill = Direct(650m, 10m);
        bill.RecordPayment("OR-1", null, null, PaymentStatus.Paid, null, PaymentStatus.Unpaid, null);

        Assert.True(bill.WouldChangeSettledBasis(UtilityCalculationBasis.Metered, UtilityCalculationBasis.DirectApproved));
        Assert.False(bill.WouldChangeSettledBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.Metered));
        Assert.Throws<InvalidOperationException>(() =>
            bill.SetCalculationBasis(UtilityCalculationBasis.Metered, UtilityCalculationBasis.DirectApproved));
    }

    [Fact]
    public void TheDtoExposesTheBasis_SoNoScreenPresentsAnApprovedAmountAsAReading()
    {
        var dto = UtilityBillDto.From(Direct(650m, 10m));

        Assert.Equal("DirectApproved", dto.ElecCalculationBasis);
        Assert.Equal("DirectApproved", dto.WaterCalculationBasis);
        Assert.Equal(650m, dto.ElecCharge);
    }
}

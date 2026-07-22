using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityRegister;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing;

public class UtilityCommandTests
{
    private static StallDto Stall(Guid id, string no, MarketSection section, string occupant, bool hasUtility = true) =>
        new(id, no, StallStatus.Active, occupant, null, null, null, 900m, 30m, null, section, null, null, null,
            HasElectricity: hasUtility, HasWater: hasUtility);

    [Fact]
    public async Task Register_ComposesRows_BilledAndUnbilled_WithTotals()
    {
        var billed = Guid.NewGuid();
        var unbilled = Guid.NewGuid();

        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetStallsByFacilityAsync(FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StallDto>
            {
                Stall(billed, "1", MarketSection.VegetableArea, "Ana Villanueva"),
                Stall(unbilled, "2", MarketSection.FishSection, "Lorna Guevarra"),
            });

        // Billed stall: 48 kWh × ₱8 = ₱384; no water. Unpaid.
        var bill = UtilityBill.Create(billed, 2026, 7, 1200, 1248, 8m, 0, 0, 0m, "admin");
        var bills = new Mock<IUtilityBillRepository>();
        bills.Setup(r => r.GetForMonthAsync(2026, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UtilityBill> { bill });

        var handler = new GetUtilityRegisterQueryHandler(stalls.Object, bills.Object);
        var result = await handler.Handle(new GetUtilityRegisterQuery(2026, 7, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(2, dto.Rows.Count);
        Assert.Equal(384m, dto.TotalDue);
        Assert.Equal(384m, dto.TotalUnpaid);
        Assert.Equal(0m, dto.TotalPaid);
        Assert.Equal(1, dto.UnpaidCount);
        Assert.Equal(1, dto.UnbilledCount);

        var billedRow = dto.Rows.Single(r => r.StallId == billed);
        Assert.True(billedRow.HasBill);
        Assert.Equal(384m, billedRow.TotalCharge);
        Assert.Equal("Unpaid", billedRow.Status);

        var unbilledRow = dto.Rows.Single(r => r.StallId == unbilled);
        Assert.False(unbilledRow.HasBill);
        Assert.Equal("Unbilled", unbilledRow.Status);
    }

    [Fact]
    public async Task Register_ExcludesLockedStalls_WithNoUtilityCharge_AndNoBill()
    {
        var metered = Guid.NewGuid();
        var locked = Guid.NewGuid();

        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetStallsByFacilityAsync(FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StallDto>
            {
                Stall(metered, "1", MarketSection.VegetableArea, "Ana Villanueva"),                 // metered
                Stall(locked, "2", MarketSection.VegetableArea, "Sari Fan", hasUtility: false),      // locked — no elec/water
            });

        var bills = new Mock<IUtilityBillRepository>();
        bills.Setup(r => r.GetForMonthAsync(2026, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UtilityBill>());

        var handler = new GetUtilityRegisterQueryHandler(stalls.Object, bills.Object);
        var result = await handler.Handle(new GetUtilityRegisterQuery(2026, 7, null), CancellationToken.None);

        var dto = result.Value!;
        // Only the metered stall appears; the locked (unmetered) stall is excluded from the utility report.
        Assert.Single(dto.Rows);
        Assert.Equal(metered, dto.Rows[0].StallId);
        Assert.Equal(1, dto.UnbilledCount);
    }

    private static (Mock<IUtilityBillRepository> bills, RecordUtilityPaymentCommandHandler handler, Mock<IUnitOfWork> uow, Mock<IEemoCacheInvalidator> cache) PaymentHandler(UtilityBill bill, bool orUnique)
    {
        var bills = new Mock<IUtilityBillRepository>();
        bills.Setup(r => r.GetByIdAsync(bill.Id, It.IsAny<CancellationToken>())).ReturnsAsync(bill);
        bills.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(orUnique);

        var collectors = new Mock<ICollectorRepository>();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.Role).Returns("Admin");
        user.SetupGet(u => u.Username).Returns("head");
        user.SetupGet(u => u.CollectorId).Returns((Guid?)null);
        var uow = new Mock<IUnitOfWork>();
        var cache = new Mock<IEemoCacheInvalidator>();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantCode).Returns("cantilan");

        var handler = new RecordUtilityPaymentCommandHandler(
            bills.Object, collectors.Object, user.Object, uow.Object, cache.Object, tenant.Object);
        return (bills, handler, uow, cache);
    }

    [Fact]
    public async Task Payment_MarksPaid_SavesAndInvalidates()
    {
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 7, 0, 10, 10m, 0, 0, 0m, "admin"); // total 100
        var (_, handler, uow, cache) = PaymentHandler(bill, orUnique: true);

        var result = await handler.Handle(
            new RecordUtilityPaymentCommand(bill.Id, PaymentStatus.Paid, null, PaymentStatus.Paid, null, "OR-U1", "OR-U1", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, bill.Status);
        Assert.Equal("OR-U1", bill.ElecORNumber);
        Assert.Equal("OR-U1", bill.WaterORNumber);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.InvalidatePaymentAffectedViewsAsync(
            "cantilan", FacilityCode.NPM, 2026, 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Payment_RejectsDuplicateOrNumber()
    {
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 7, 0, 10, 10m, 0, 0, 0m, "admin");
        var (_, handler, uow, _) = PaymentHandler(bill, orUnique: false);

        var result = await handler.Handle(
            new RecordUtilityPaymentCommand(bill.Id, PaymentStatus.Paid, null, PaymentStatus.Paid, null, "OR-DUP", "OR-DUP", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(PaymentStatus.Unpaid, bill.Status); // unchanged
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

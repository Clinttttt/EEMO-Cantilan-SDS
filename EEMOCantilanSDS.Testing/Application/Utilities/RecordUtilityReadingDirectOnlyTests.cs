using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Utilities
{
    /// <summary>
    /// IA-055 at the writer: a new ECF/WCF assessment is a direct approved amount only. New meter readings or per-unit
    /// rates are refused server-side whatever the client sends, while a recorded metered month stays exactly as recorded
    /// (it may be resubmitted unchanged or restated as a direct amount, never re-read).
    /// </summary>
    public class RecordUtilityReadingDirectOnlyTests
    {
        private sealed class Fixture
        {
            public Stall Stall { get; }
            public Mock<IUtilityBillRepository> Bills { get; } = new();
            public Mock<IUnitOfWork> Uow { get; } = new();
            public UtilityBill? Added { get; private set; }
            public RecordUtilityReadingCommandHandler Handler { get; }

            public Fixture(UtilityBill? existing, FacilityCode facility = FacilityCode.NPM)
            {
                var f = Facility.Create(facility, "Market", "MKT");
                Stall = Stall.Create(f.Id, "12", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);
                typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(Stall, f);

                var stalls = new Mock<IStallRepository>();
                stalls.Setup(s => s.GetByIdAsync(Stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Stall);
                Bills.Setup(r => r.GetByStallAndMonthAsync(Stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(existing);
                Bills.Setup(r => r.AddAsync(It.IsAny<UtilityBill>(), It.IsAny<CancellationToken>()))
                    .Callback<UtilityBill, CancellationToken>((b, _) => Added = b)
                    .Returns(Task.CompletedTask);
                var user = new Mock<ICurrentUserService>();
                user.SetupGet(u => u.Username).Returns("office");
                var tenant = new Mock<ITenantContext>();
                tenant.SetupGet(t => t.TenantCode).Returns("tenant-a");

                Handler = new RecordUtilityReadingCommandHandler(
                    Bills.Object, stalls.Object, user.Object, Uow.Object, new Mock<IEemoCacheInvalidator>().Object, tenant.Object);
            }

            public Task<Result<EEMOCantilanSDS.Application.Dtos.Utilities.UtilityBillDto>> Send(
                decimal ep, decimal ec, decimal er, decimal wp, decimal wc, decimal wr,
                decimal? elecApproved = null, decimal? waterApproved = null) =>
                Handler.Handle(new RecordUtilityReadingCommand(Stall.Id, 2026, 10, ep, ec, er, wp, wc, wr, null,
                    elecApproved, waterApproved), CancellationToken.None);

            public void NothingWritten()
            {
                Bills.Verify(r => r.AddAsync(It.IsAny<UtilityBill>(), It.IsAny<CancellationToken>()), Times.Never);
                Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        private static UtilityBill RecordedMetered() =>
            UtilityBill.Create(Guid.NewGuid(), 2026, 10, 1200m, 1248m, 8m, 40m, 56m, 5m, "historical");

        [Fact]
        public async Task NewMonth_ElectricityMeterReadings_AreRefused_AndNothingIsWritten()
        {
            var fx = new Fixture(existing: null);

            var result = await fx.Send(1200m, 1248m, 8m, 0m, 0m, 0m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            Assert.Contains("Electricity", result.Error);
            fx.NothingWritten();
        }

        [Fact]
        public async Task NewMonth_WaterPerUnitRateAlone_IsRefused()
        {
            var fx = new Fixture(existing: null);

            var result = await fx.Send(0m, 0m, 0m, 0m, 0m, 5m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            Assert.Contains("Water", result.Error);
            fx.NothingWritten();
        }

        [Fact]
        public async Task NewMonth_ReadingsAreRefused_EvenWhenTheOtherPartIsDirect()
        {
            var fx = new Fixture(existing: null);

            var result = await fx.Send(0m, 0m, 0m, 40m, 56m, 5m, elecApproved: 650m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            fx.NothingWritten();
        }

        [Fact]
        public async Task NewMonth_DirectAmounts_AreRecordedAsDirectApproved_AtExactlyTheApprovedAmount()
        {
            var fx = new Fixture(existing: null);

            // The reading arguments a stale client may still send are ignored for a part stated as a direct amount.
            var result = await fx.Send(1200m, 1248m, 8m, 40m, 56m, 5m, elecApproved: 650m, waterApproved: 10m);

            Assert.True(result.IsSuccess);
            var bill = Assert.IsType<UtilityBill>(fx.Added);
            Assert.Equal(UtilityCalculationBasis.DirectApproved, bill.ElecCalculationBasis);
            Assert.Equal(UtilityCalculationBasis.DirectApproved, bill.WaterCalculationBasis);
            Assert.Equal(650m, bill.ElecCharge);
            Assert.Equal(10m, bill.WaterCharge);
            fx.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NewMonth_DirectElectricity_WithAnUnbilledWater_IsAccepted()
        {
            var fx = new Fixture(existing: null);

            var result = await fx.Send(0m, 0m, 0m, 0m, 0m, 0m, elecApproved: 650m);

            Assert.True(result.IsSuccess);
            Assert.Equal(650m, fx.Added!.ElecCharge);
            Assert.Equal(0m, fx.Added.WaterCharge);
        }

        [Fact]
        public async Task RecordedMeteredMonth_ResubmittedUnchanged_KeepsItsEvidenceAndBasis()
        {
            var bill = RecordedMetered();
            var fx = new Fixture(bill);

            var result = await fx.Send(1200m, 1248m, 8m, 40m, 56m, 5m);

            Assert.True(result.IsSuccess);
            Assert.Equal(UtilityCalculationBasis.Metered, bill.ElecCalculationBasis);
            Assert.Equal(UtilityCalculationBasis.Metered, bill.WaterCalculationBasis);
            Assert.Equal((1200m, 1248m, 8m), (bill.ElecPreviousReading, bill.ElecCurrentReading, bill.ElecRatePerKwh));
            Assert.Equal(384m, bill.ElecCharge);
            Assert.Equal(80m, bill.WaterCharge);
            Assert.Equal(1L, bill.ElectricitySourceVersion);
            Assert.Equal(1L, bill.WaterSourceVersion);
        }

        [Fact]
        public async Task RecordedMeteredMonth_ChangedReadings_AreRefused_AndTheEvidenceIsUntouched()
        {
            var bill = RecordedMetered();
            var fx = new Fixture(bill);

            var result = await fx.Send(1200m, 1300m, 8m, 40m, 56m, 5m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            Assert.Contains("kept as recorded", result.Error);
            Assert.Equal(1248m, bill.ElecCurrentReading);
            Assert.Equal(384m, bill.ElecCharge);
            fx.NothingWritten();
        }

        [Fact]
        public async Task RecordedMeteredMonth_ChangedRate_IsRefused()
        {
            var bill = RecordedMetered();
            var fx = new Fixture(bill);

            var result = await fx.Send(1200m, 1248m, 8m, 40m, 56m, 6m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            Assert.Equal(5m, bill.WaterRatePerCubicMeter);
            fx.NothingWritten();
        }

        [Fact]
        public async Task RecordedMeteredMonth_MayBeRestatedAsADirectAmount_WhileTheOtherPartStaysAsRecorded()
        {
            var bill = RecordedMetered();
            var fx = new Fixture(bill);

            var result = await fx.Send(0m, 0m, 0m, 40m, 56m, 5m, elecApproved: 400m);

            Assert.True(result.IsSuccess);
            Assert.Equal(UtilityCalculationBasis.DirectApproved, bill.ElecCalculationBasis);
            Assert.Equal(400m, bill.ElecCharge);
            Assert.Equal(UtilityCalculationBasis.Metered, bill.WaterCalculationBasis);
            Assert.Equal(80m, bill.WaterCharge);
        }

        [Fact]
        public async Task RecordedDirectPart_SentBackAsItsStoredReadings_StaysDirectApproved()
        {
            // A client that hides a part it does not bill sends that part's stored values back unchanged.
            var (p, c, r) = UtilityBill.DirectApprovedReadings(10m);
            var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 10, 0m, 0m, 0m, p, c, r, "wcf");
            bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.DirectApproved);
            var fx = new Fixture(bill);

            var result = await fx.Send(0m, 0m, 0m, p, c, r, elecApproved: 650m);

            Assert.True(result.IsSuccess);
            Assert.Equal(UtilityCalculationBasis.DirectApproved, bill.WaterCalculationBasis);
            Assert.Equal(10m, bill.WaterCharge);
            Assert.Equal(650m, bill.ElecCharge);
        }

        [Fact]
        public async Task RecordedDirectPart_CannotBeConvertedToMeterReadings()
        {
            var (p, c, r) = UtilityBill.DirectApprovedReadings(650m);
            var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 10, p, c, r, 0m, 0m, 0m, "office");
            bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.Metered);
            var fx = new Fixture(bill);

            var result = await fx.Send(1200m, 1248m, 8m, 0m, 0m, 0m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            Assert.Equal(UtilityCalculationBasis.DirectApproved, bill.ElecCalculationBasis);
            Assert.Equal(650m, bill.ElecCharge);
            fx.NothingWritten();
        }

        [Fact]
        public async Task RecordedDirectPart_CannotBeZeroedBySendingEmptyReadings()
        {
            var (p, c, r) = UtilityBill.DirectApprovedReadings(650m);
            var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 10, p, c, r, 0m, 0m, 0m, "office");
            bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.Metered);
            var fx = new Fixture(bill);

            var result = await fx.Send(0m, 0m, 0m, 0m, 0m, 0m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            Assert.Equal(650m, bill.ElecCharge);
            fx.NothingWritten();
        }

        [Fact]
        public async Task NonNpmStall_IsRefusedBeforeAnyAssessment()
        {
            var fx = new Fixture(existing: null, facility: FacilityCode.TCC);

            var result = await fx.Send(0m, 0m, 0m, 0m, 0m, 0m, elecApproved: 650m);

            Assert.Equal(ResultStatus.Invalid, result.Status);
            fx.NothingWritten();
        }
    }
}

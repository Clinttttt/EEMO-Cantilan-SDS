using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Utilities
{
    /// <summary>
    /// The utility-bill entry seed (IA-055). A new month is a direct approved amount only: nothing is carried from an
    /// earlier meter, no per-unit rate is suggested, and the server says so through the allowed bases. A month already on
    /// record returns its own figures; a recorded metered part lists Metered so its readings can be resubmitted as they are.
    /// </summary>
    public class GetUtilityBillForEntryQueryHandlerTests
    {
        private static readonly Guid Stall = Guid.NewGuid();

        private static Mock<IUtilityBillRepository> Repo(UtilityBill? current, UtilityBill? prior = null)
        {
            var repo = new Mock<IUtilityBillRepository>();
            repo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(current);
            repo.Setup(r => r.GetLatestBeforeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prior);
            return repo;
        }

        [Fact]
        public async Task NewMonth_AfterAMeteredMonth_IsADirectAmountOnly_WithNothingCarriedFromTheMeter()
        {
            var meteredPrior = UtilityBill.Create(Stall, 2026, 9, 1200m, 1248m, 8m, 40m, 56m, 5m);
            var handler = new GetUtilityBillForEntryQueryHandler(Repo(null, meteredPrior).Object);

            var result = await handler.Handle(new GetUtilityBillForEntryQuery(Stall, 2026, 10), CancellationToken.None);

            var seed = result.Value!;
            Assert.False(seed.Exists);
            Assert.Equal("DirectApproved", seed.ElecCalculationBasis);
            Assert.Equal("DirectApproved", seed.WaterCalculationBasis);
            Assert.Equal(new[] { "DirectApproved" }, seed.AllowedElecCalculationBases);
            Assert.Equal(new[] { "DirectApproved" }, seed.AllowedWaterCalculationBases);
            Assert.Equal(0m, seed.ElecPreviousReading);
            Assert.Equal(0m, seed.ElecRatePerKwh);
            Assert.Equal(0m, seed.WaterPreviousReading);
            Assert.Equal(0m, seed.WaterRatePerCubicMeter);
        }

        [Fact]
        public async Task RecordedMeteredMonth_ReturnsItsReadings_AndListsMeteredOnlyForTheMeteredPart()
        {
            // Electricity read from the meter; water never billed this month.
            var recorded = UtilityBill.Create(Stall, 2026, 8, 1200m, 1248m, 8m, 0m, 0m, 0m);
            var handler = new GetUtilityBillForEntryQueryHandler(Repo(recorded).Object);

            var seed = (await handler.Handle(new GetUtilityBillForEntryQuery(Stall, 2026, 8), CancellationToken.None)).Value!;

            Assert.True(seed.Exists);
            Assert.Equal(1248m, seed.ElecCurrentReading);
            Assert.Equal(8m, seed.ElecRatePerKwh);
            Assert.Equal(new[] { "DirectApproved", "Metered" }, seed.AllowedElecCalculationBases);
            Assert.Equal(new[] { "DirectApproved" }, seed.AllowedWaterCalculationBases);
        }
    }
}

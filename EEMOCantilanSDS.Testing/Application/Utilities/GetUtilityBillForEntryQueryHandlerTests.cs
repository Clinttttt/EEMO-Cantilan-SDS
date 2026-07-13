using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Utilities
{
    /// <summary>
    /// The utility-bill entry seed. A brand-new month (no prior bill) must pre-fill the electricity/water
    /// rate from the tenant's configured ordinance rate (ElecPerKwh / WaterPerCubicMeter), so the modal
    /// shows the LGU's real rate instead of ₱0. A tenant with no such rows (Cantilan) resolves to 0 and is
    /// left unchanged.
    /// </summary>
    public class GetUtilityBillForEntryQueryHandlerTests
    {
        private static readonly Guid Stall = Guid.NewGuid();

        private static IFeeRateResolver ResolverWith(params FeeRateEntry[] entries)
        {
            var mock = new Mock<IFeeRateResolver>();
            mock.Setup(r => r.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FeeRateSnapshot(entries));
            return mock.Object;
        }

        private static Mock<IUtilityBillRepository> RepoWithNoBills()
        {
            var repo = new Mock<IUtilityBillRepository>();
            repo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UtilityBill?)null);
            repo.Setup(r => r.GetLatestBeforeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UtilityBill?)null);
            return repo;
        }

        [Fact]
        public async Task NewBill_ConfiguredTenant_PrefillsOrdinanceElecAndWaterRates()
        {
            var repo = RepoWithNoBills();
            var resolver = ResolverWith(
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.ElecPerKwh, 1.00m, new DateOnly(2026, 1, 1)),
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.WaterPerCubicMeter, 1.00m, new DateOnly(2026, 1, 1)));

            var handler = new GetUtilityBillForEntryQueryHandler(repo.Object, resolver);
            var result = await handler.Handle(new GetUtilityBillForEntryQuery(Stall, 2026, 7), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value!.Exists);
            Assert.Equal(1.00m, result.Value.ElecRatePerKwh);
            Assert.Equal(1.00m, result.Value.WaterRatePerCubicMeter);
        }

        [Fact]
        public async Task NewBill_TenantWithNoRateRows_StaysZero()
        {
            var repo = RepoWithNoBills();
            var resolver = ResolverWith(); // no rows — e.g. Cantilan (metered add-ons unset)

            var handler = new GetUtilityBillForEntryQueryHandler(repo.Object, resolver);
            var result = await handler.Handle(new GetUtilityBillForEntryQuery(Stall, 2026, 7), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value!.Exists);
            Assert.Equal(0m, result.Value.ElecRatePerKwh);
            Assert.Equal(0m, result.Value.WaterRatePerCubicMeter);
        }
    }
}

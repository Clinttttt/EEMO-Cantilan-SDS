using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Services;
using Moq;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public sealed class MobileOfflineMenuColdStartTests
{
    [Fact]
    public async Task Online_menu_preparation_persists_governed_terms_for_first_offline_navigation_after_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stalltrack-offline-readiness-" + Guid.NewGuid().ToString("N"));
        try
        {
            var terms = new GovernedServiceTermsDto("MARKET_FEES", "Market Fees", false,
                GovernedServiceBasis.ApprovedFeeOption, null, null, RevenueInstrumentType.CashTicket,
                false, null, []);
            var online = new Mock<IMobileApiClient>(MockBehavior.Strict);
            online.Setup(x => x.GetOperationTermsAsync("MARKET_FEES", null))
                .ReturnsAsync(Result<GovernedServiceTermsDto>.Success(terms));
            var onlineCache = new JsonOfflineReadCache(directory);
            var onlineApi = new CachingMobileApiClient(online.Object, onlineCache, new FakeConnectivityMonitor(online: true));
            using (var readiness = new OfflineReadinessCoordinator(onlineApi, new FakeConnectivityMonitor(online: true)))
            {
                var menu = new MobileMenuDto(Guid.NewGuid(), "Collector", "001", DateOnly.FromDateTime(DateTime.Today), []);
                readiness.Start(menu, [new("MARKET_FEES", "Market Fees", true,
                    CollectorOperationCapabilityStatus.Ready, true, [])]);
                for (var i = 0; i < 150 && readiness.IsPreparing; i++) await Task.Delay(10);
                Assert.False(readiness.IsPreparing);
                Assert.Equal(1, readiness.Completed);
                Assert.Equal(0, readiness.Failed);
            }
            // This is a *new cache instance*, like a freshly restarted app with no screens visited.
            var noNetwork = new Mock<IMobileApiClient>(MockBehavior.Strict);
            var reopenedCache = new JsonOfflineReadCache(directory);
            var offlineApi = new CachingMobileApiClient(noNetwork.Object, reopenedCache, new FakeConnectivityMonitor(online: false));
            var result = await offlineApi.GetOperationTermsAsync("MARKET_FEES", null);
            Assert.True(result.IsSuccess);
            Assert.Equal("Market Fees", result.Value!.Name);
            noNetwork.VerifyNoOtherCalls();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

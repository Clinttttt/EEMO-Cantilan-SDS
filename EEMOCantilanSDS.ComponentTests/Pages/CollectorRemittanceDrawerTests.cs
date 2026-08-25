using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorRemittances;
using EEMOCantilanSDS.Application.Requests.Collectors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Collector = EEMOCantilanSDS.Client.Components.Pages.Menus.Collector;

/// <summary>
/// Recording cash a collector has turned in.
///
/// <para>
/// Two things about this screen are worth pinning. It states the position BEFORE the amount is typed, because the office's
/// rule is that a remittance never exceeds what was collected in those days, and an officer cannot honour a rule they
/// cannot see. And a refusal from the server is shown as written: those messages name the figures and the days so the
/// officer can act on them, and summarising them into "invalid" would throw that away.
/// </para>
/// </summary>
public class CollectorRemittanceDrawerTests : TestContext
{
    private static readonly Guid CollectorId = Guid.NewGuid();

    private static CollectorListDto Roster() => new(
        CollectorId, "Juan Dels", "juan@example.gov.ph", "EEMO-2026-001",
        new List<FacilityCode> { FacilityCode.NPM }, 420m, 14, DateTime.UtcNow, true);

    private static CollectorRemittanceSummaryDto Position(decimal collected, decimal remitted) => new(
        CollectorId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 25),
        collected, remitted, collected - remitted,
        remitted > 0m
            ? new[]
            {
                new CollectorRemittanceLineDto(
                    Guid.NewGuid(), new DateTime(2026, 8, 22, 16, 40, 0), remitted,
                    new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 21), "head", "RCD-2026-08-014", null)
            }
            : Array.Empty<CollectorRemittanceLineDto>());

    private IRenderedComponent<Collector> RenderPage(Mock<ICollectorsApiClient> collectors)
    {
        collectors.Setup(c => c.GetAllCollectorsAsync())
                  .ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[] { Roster() }));
        collectors.Setup(c => c.GetCollectorByIdAsync(CollectorId))
                  .ReturnsAsync(Result<CollectorActivityDto>.Success(new CollectorActivityDto(
                      CollectorId, "Juan Dels", "EEMO-2026-001", "juan@example.gov.ph", "09170000000",
                      new List<FacilityCode> { FacilityCode.NPM }, 420m, 14, 1, DateTime.UtcNow,
                      new List<RecentTransactionDto>())));

        Services.AddSingleton(collectors.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IMfaApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Cly Sullano");
        auth.SetRoles("SuperAdmin");

        return RenderComponent<Collector>();
    }

    private static void OpenDrawer(IRenderedComponent<Collector> cut)
    {
        // The roster arrives asynchronously, so the row's controls are waited for rather than assumed.
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button.action-btn"),
            b => (b.GetAttribute("title") ?? string.Empty) == "View Activity"));

        cut.FindAll("button.action-btn")
           .First(b => (b.GetAttribute("title") ?? string.Empty) == "View Activity")
           .Click();

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll("button.btn-primary"),
            b => b.TextContent.Contains("Record Remittance", StringComparison.Ordinal)));

        cut.FindAll("button.btn-primary")
           .First(b => b.TextContent.Contains("Record Remittance", StringComparison.Ordinal))
           .Click();
    }

    [Fact]
    public void StatesThePositionBeforeAnAmountIsTyped()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(c => c.GetCollectorRemittancesAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                  .ReturnsAsync(Result<CollectorRemittanceSummaryDto>.Success(Position(420m, 300m)));

        var cut = RenderPage(collectors);
        OpenDrawer(cut);

        cut.WaitForAssertion(() =>
        {
            var figures = cut.FindAll(".activity-stats-3 .act-stat");
            Assert.Equal(3, figures.Count);
            Assert.Contains("420.00", figures[0].TextContent, StringComparison.Ordinal);   // collected
            Assert.Contains("300.00", figures[1].TextContent, StringComparison.Ordinal);   // already remitted
            Assert.Contains("120.00", figures[2].TextContent, StringComparison.Ordinal);   // still held
        });
    }

    [Fact]
    public void SayingNothingIsNotAnOption_TheAmountMustBeEntered()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(c => c.GetCollectorRemittancesAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                  .ReturnsAsync(Result<CollectorRemittanceSummaryDto>.Success(Position(420m, 0m)));

        var cut = RenderPage(collectors);
        OpenDrawer(cut);

        cut.WaitForAssertion(() =>
        {
            var save = cut.FindAll("button.btn-primary")
                          .First(b => b.TextContent.Contains("Record Remittance", StringComparison.Ordinal));
            Assert.True(save.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void ARefusalFromTheServerIsShownAsWritten()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(c => c.GetCollectorRemittancesAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                  .ReturnsAsync(Result<CollectorRemittanceSummaryDto>.Success(Position(420m, 0m)));
        collectors.Setup(c => c.RecordCollectorRemittanceAsync(CollectorId, It.IsAny<RecordCollectorRemittanceRequest>()))
                  .ReturnsAsync(Result<RemittanceRecordedDto>.Failure(
                      "₱500.00 is more than the ₱420.00 collected from Aug 1, 2026 to Aug 25, 2026.", ResultStatus.Invalid));

        var cut = RenderPage(collectors);
        OpenDrawer(cut);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[type=number]")));
        cut.Find("input[type=number]").Input("500");
        cut.FindAll("button.btn-primary")
           .First(b => b.TextContent.Contains("Record Remittance", StringComparison.Ordinal))
           .Click();

        cut.WaitForAssertion(() =>
        {
            var error = cut.Find(".form-error").TextContent;
            Assert.Contains("more than the", error, StringComparison.Ordinal);
            Assert.Contains("420.00", error, StringComparison.Ordinal);
        });
    }
}

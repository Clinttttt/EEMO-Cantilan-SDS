using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Client.Components.Pages.Shared.Actions;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public class PaymentHistoryDelinquencyTests : TestContext
{
    [Fact]
    public void UnpaidAggregateIsNotMisrepresentedAsArrearsOrElapsedMonthDelinquency()
    {
        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(api => api.GetPaymentHistoryAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success(Array.Empty<PaymentHistoryDto>()));
        Services.AddSingleton(payments.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton<FacilityState>();

        var stallId = Guid.NewGuid();
        var cut = RenderComponent<PaymentHistoryModal<StallLike>>(parameters => parameters
            .Add(component => component.Show, true)
            .Add(component => component.Stall, new StallLike
            {
                StallGuid = stallId,
                StallNo = "04",
                ActualOccupant = "Test Occupant",
                MonthlyRate = 900m,
                LedgerSummary = new StallLedgerSummaryDto(0, 1, 0m, 900m)
            }));

        Assert.Equal("Unpaid Balance", cut.Find(".ph-status-badge").TextContent.Trim());
        Assert.DoesNotContain("ph-badge-delinquent", cut.Find(".ph-status-badge").GetAttribute("class"));
        Assert.DoesNotContain("With Arrears", cut.Markup);
    }

    private sealed class StallLike
    {
        public Guid StallGuid { get; init; }
        public string StallNo { get; init; } = string.Empty;
        public string? ActualOccupant { get; init; }
        public string? ContractName { get; init; }
        public decimal MonthlyRate { get; init; }
        public decimal? PartialAmount { get; init; }
        public bool IsPartial { get; init; }
        public DateTime? ContractDate { get; init; }
        public StallLedgerSummaryDto? LedgerSummary { get; init; }
    }
}

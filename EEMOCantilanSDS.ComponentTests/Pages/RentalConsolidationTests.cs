using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Pages.Shared;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ClosedAccounts = EEMOCantilanSDS.Client.Components.Pages.Reports.ClosedAccounts;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class RentalConsolidationTests : TestContext
{
    private readonly Mock<IObligationsApiClient> _accounts = new();
    private readonly Mock<IStallsApiClient> _stalls = new();
    private readonly Mock<IPaymentsApiClient> _payments = new();
    private readonly Mock<ICollectionActivityApiClient> _activity = new();
    private readonly Mock<IVendorsApiClient> _vendors = new();
    public RentalConsolidationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Head").SetRoles("SuperAdmin");
        Services.AddSingleton(_accounts.Object); Services.AddSingleton(_stalls.Object);
        Services.AddSingleton(_payments.Object); Services.AddSingleton(_activity.Object); Services.AddSingleton(_vendors.Object);
        Services.AddSingleton(Mock.Of<IEcfCollectionsApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>()); Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<BrandingState>(); Services.AddSingleton<FacilityState>();
        _accounts.Setup(x => x.GetWorkspaceAsync(It.IsAny<ObligationKind>())).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([], 0m, 0m, 0m)));
        _accounts.Setup(x => x.GetSpaceOperationsAsync()).ReturnsAsync(Result<IReadOnlyList<SpaceRentalOperationDto>>.Success([
            new(ObligationKind.KanmanggaySpaceRental, null, "Kanmanggay", true),
            new(ObligationKind.FiestaArawLotRental, LotRentalEvent.Fiesta, "Fiesta", false),
            new(ObligationKind.VegetableFruitSpaceRental, null, "Vegetable / Fruits", true)]));
        _stalls.Setup(x => x.GetClosedStallAccountsAsync()).ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success([]));
        _stalls.Setup(x => x.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Failure("Rates unavailable"));
    }
    private static ObligationAccountDto Account(ObligationKind kind = ObligationKind.KanmanggaySpaceRental, bool closed = false) => new(
        Guid.NewGuid(), kind, kind.ToString(), Guid.Empty, "Same name", null, null, "007",
        kind == ObligationKind.FiestaArawLotRental ? LotRentalEvent.Fiesta : null,
        kind == ObligationKind.FiestaArawLotRental ? new(2026, 8, 15) : null,
        kind == ObligationKind.FiestaArawLotRental ? new(2026, 8, 15) : new(2026, 1, 1), closed ? new(2026, 9, 30) : null, 900m, new(2026, 1, 1),
        1800m, 400m, 1400m, OccupancyArrangement.SignedContract, "LEASE-REF", "Source occupant");
    private void Serve(params ObligationAccountDto[] accounts)
    {
        foreach (var group in accounts.GroupBy(x => x.Kind))
            _accounts.Setup(x => x.GetWorkspaceAsync(group.Key)).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(
                new(group.ToArray(), group.Sum(x => x.AssessedToDate), group.Sum(x => x.CollectedToDate), group.Sum(x => x.OutstandingToDate))));
    }
    private static void Click(IRenderedFragment cut, string text) => cut.FindAll("button").Single(x => x.TextContent.Trim() == text).Click();

    [Theory]
    [InlineData("ICE")]
    [InlineData("NCC")]
    public void FacilityAndKmUseTheActualSameFormSections(string facility)
    {
        var form = new AddVendorModal.VendorModalForm { FacilityCode = facility, StallNo = "017", ActualOccupant = "Facility occupant", ContractName = "Signed name", MonthlyRate = 900m };
        var original = RenderComponent<AddVendorModal>(p => p.Add(x => x.Show, true).Add(x => x.Form, form).Add(x => x.FacilityLocked, true)
            .Add(x => x.FacilityOptions, [new() { Code = facility, Name = facility }]));
        var km = RenderComponent<Kanmanggay>(); Click(km, "+ Add New");
        Assert.NotNull(original.FindComponent<RentalLesseeFields>()); Assert.NotNull(km.FindComponent<RentalLesseeFields>());
        Assert.NotNull(original.FindComponent<RentalContractFields>()); Assert.NotNull(km.FindComponent<RentalContractFields>());
        Assert.NotNull(original.FindComponent<RentalUnitNumberField>()); Assert.True(km.FindComponent<RentalUnitNumberField>().Instance.ServerAssigned);
        Assert.NotNull(original.FindComponent<RentalEffectivityField>()); Assert.False(km.FindComponent<RentalEffectivityField>().Instance.MonthPrecision);
        Assert.Equal(2, original.FindComponents<RentalRateFields>().Count); Assert.Equal(2, km.FindComponents<RentalRateFields>().Count);
        Assert.Single(original.FindAll("input[placeholder='As written on the signed contract']"));
        Assert.Single(km.FindAll("input[placeholder='As written on the signed contract']"));
        Assert.Contains("Contract Duration (No. of Years)", km.Markup);
        Assert.Contains("Space Information", km.Markup);
        Assert.Contains("Additional Information", km.Markup);
        Assert.Contains("Space No.", km.FindComponent<RentalContractFields>().Markup);
        Assert.Empty(km.FindComponent<RentalUnitNumberField>().FindAll("input"));
        Assert.Single(km.FindAll("input[type='date']"));
        Assert.Equal(form.StallNo, original.FindComponent<RentalUnitNumberField>().Find("input").GetAttribute("value"));
        Assert.Equal(PhilippineTime.Today.ToString("yyyy-MM-dd"), km.Find("#obw-contract-date").GetAttribute("value"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact(facility.ToLowerInvariant() + "-shared-registration", original.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("km-shared-registration", km.Markup);
    }

    [Fact]
    public void KmRentalInputAndChargeCardSaveOneAmountAndNeverMisuseContractName()
    {
        var account = Account(); CreateObligationAccountRequest? request = null;
        _accounts.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).Callback<CreateObligationAccountRequest>(x => request = x)
            .ReturnsAsync(Result<ObligationAccountDto>.Success(account));
        _accounts.SetupSequence(x => x.GetWorkspaceAsync(account.Kind))
            .ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([], 0m, 0m, 0m)))
            .ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([account], 1800m, 400m, 1400m)));
        var cut = RenderComponent<Kanmanggay>(); Click(cut, "+ Add New");
        cut.Find("#obw-occupant").Input("Source occupant");
        cut.Find("input[placeholder='As written on the contract']").Change("LEASE-REF");
        cut.Find("#obw-contract-date").Change("2026-09-01");
        cut.Find("input[placeholder='As written on the signed contract']").Input("Named contract lessee");
        cut.Find("#obw-amount").Change("900");
        Assert.Equal("900", cut.Find(".avm-spin-input").GetAttribute("value"));
        cut.Find("button[aria-label='Increase monthly rental']").Click();
        Assert.Equal("1000", cut.Find("#obw-amount").GetAttribute("value"));
        Assert.Contains("₱12,000.00", cut.Find(".avm-yearly-value").TextContent);
        cut.Find(".avm-spin-input").Change("1100.50");
        Assert.Equal("1100.50", cut.Find("#obw-amount").GetAttribute("value"));
        cut.Find("form[aria-label='Open account']").Submit();
        Assert.NotNull(request); Assert.Equal(1100.50m, request!.Amount); Assert.Equal("LEASE-REF", request.ContractReference);
        Assert.Equal("Source occupant", request.ActualOccupant); Assert.Equal(new DateOnly(2026, 9, 1), request.ActiveFrom);
        Assert.Equal("Named contract lessee", request.ContractName);
        Assert.Equal(new DateOnly(2026, 9, 1), request.ContractEffectiveDate);
        Assert.Equal(3, request.ContractYears);
        Assert.Equal("", request.SubjectLabel); Assert.Null(request.StallId); Assert.Equal(Guid.Empty, request.PayorId);
        Assert.Contains("007", cut.Find("tbody").TextContent);
        _accounts.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
        Assert.Empty(_payments.Invocations); Assert.Empty(_stalls.Invocations);
    }

    [Fact]
    public void KmThreeOverviewActionsKeepAccountAndPeriodIdentity()
    {
        var account = Account(); Serve(account);
        var period = new ObligationQuoteDto(account.Id, Guid.NewGuid(), account.Kind, account.KindLabel, account.SubjectLabel,
            new(2026, 9, 1), 900m, 400m, 500m, Guid.Empty, "Same name", null, true);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([period]));
        var cut = RenderComponent<Kanmanggay>();
        Assert.NotNull(cut.FindComponent<RegisteredRentalTable<ObligationAccountDto>>());
        Assert.NotNull(cut.FindComponent<RentalSummaryStrip>());
        Assert.Equal(new[] { "₱", "↺", "→" }, cut.FindAll("tbody button").Select(x => x.TextContent));
        Click(cut, "₱");
        Assert.Contains("not a receipt", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("₱500.00", cut.Find("[role='dialog'] tbody").TextContent);
        _accounts.Verify(x => x.GetRegisterAsync(account.Id), Times.Once);
        Assert.Empty(_activity.Invocations); Assert.Empty(_payments.Invocations);
        Click(cut, "Close"); Click(cut, "→");
        Assert.EndsWith($"/operations/kanmanggay/manage-space-holders?account={account.Id}", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.DoesNotContain(_accounts.Invocations, x => x.Method.Name == nameof(IObligationsApiClient.CreateAccountAsync));
    }

    [Fact]
    public void HistoryMatchesPeriodIdsNotNamesAndPreservesSrcCorrectionEvidence()
    {
        var account = Account(); Serve(account); var periodId = Guid.NewGuid();
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([
            new(account.Id, periodId, account.Kind, account.KindLabel, account.SubjectLabel, new(2026, 9, 1), 900m, 400m, 500m, Guid.Empty, "Same name", null, true)]));
        var correction = new CollectionActivityCorrectionDto(Guid.NewGuid(), "Reversal", PhilippineTime.Today, DateTime.UtcNow, -400m, Guid.NewGuid(), "Authorized correction");
        CollectionActivityEventDto Event(Guid source, string src) => new(src, "Canonical", "Space Rental", Guid.NewGuid(), PhilippineTime.Today,
            DateTime.UtcNow, src, "OR-17", RevenueInstrumentType.OfficialReceipt, [], ["SRC-REPLACEMENT"], "Same name", null, null,
            "Collector", "Head", null, "007", null, 400m, -400m, 0m, "Reversed", null,
            [new("SPACE_RENTAL", "Space Rental", 400m, nameof(CollectionSourceKind.ObligationPeriod), null, source, 2026, 9, "Rent")], [correction]);
        _activity.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, 500, null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new(PhilippineTime.Today, PhilippineTime.Today, "BusinessDate", "EffectiveDate", DateTime.UtcNow,
                [Event(periodId, "SRC-CORRECT"), Event(Guid.NewGuid(), "SRC-OTHER-ACCOUNT")], 2, true, 0m, 800m, -800m, 0m, [])));
        var cut = RenderComponent<Kanmanggay>(); Click(cut, "↺");
        Assert.Contains("SRC-CORRECT", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("SRC-REPLACEMENT", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("Authorized correction", cut.Find("[role='dialog']").TextContent);
        Assert.DoesNotContain("SRC-OTHER-ACCOUNT", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("activity feed was limited", cut.Find("[role='dialog']").TextContent);
        Assert.Empty(_payments.Invocations);
        var call = Assert.Single(_activity.Invocations); var from = (DateOnly)call.Arguments[0]; var to = (DateOnly)call.Arguments[1];
        Assert.InRange(to.DayNumber - from.DayNumber, 27, 30);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("km-history", cut.Markup);
    }

    [Theory]
    [InlineData("KM", ObligationKind.KanmanggaySpaceRental, "Space")]
    [InlineData("FA", ObligationKind.FiestaArawLotRental, "Lot")]
    [InlineData("Vegetable/Fruits", ObligationKind.VegetableFruitSpaceRental, "Space")]
    public void UnifiedRegistryUsesSourceAdaptersAndPreservesDeepLinks(string code, ObligationKind kind, string unit)
    {
        var account = Account(kind, true); Serve(account);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?workspace={Uri.EscapeDataString(code)}&account={account.Id}");
        var cut = RenderComponent<Vendor>();
        Assert.NotNull(cut.FindComponent<RentalRegistryTable<ObligationAccountDto>>());
        Assert.Contains(unit + " No.", cut.Find("thead").TextContent);
        Assert.Contains(account.Id.ToString(), cut.Find("[aria-label='Account profile']").TextContent);
        Assert.Equal("Closed", cut.Find("td[data-label='Status'] span").TextContent);
        Assert.Equal("Partial", cut.Find("td[data-label='Payment'] span").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), x => x.TextContent is "Change amount" or "Close account" or "Reopen account" or "Renew");
        Assert.Empty(_stalls.Invocations); Assert.Empty(_vendors.Invocations);
        Assert.Equal(code == "Vegetable/Fruits" ? "VF" : code, cut.Find("#rental-workspace-picker strong").TextContent.Trim());
        Assert.Single(cut.FindAll(".toolbar-unified"));
        Assert.Empty(cut.FindAll(".rental-picker"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact(code == "FA" ? "fa-registry" : code == "KM" ? "km-registry" : "vf-registry", cut.Markup);
    }

    [Theory]
    [InlineData("KM", ObligationKind.KanmanggaySpaceRental)]
    [InlineData("Vegetable/Fruits", ObligationKind.VegetableFruitSpaceRental)]
    [InlineData("FA", ObligationKind.FiestaArawLotRental)]
    public void SpaceRegistryReusesAllFiveFacilityActionIconsAndOwnAccountHandlers(string code, ObligationKind kind)
    {
        var account = Account(kind); Serve(account);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id))
            .ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?workspace={Uri.EscapeDataString(code)}");
        var cut = RenderComponent<Vendor>();

        var buttons = cut.FindAll(".registry-actions button");
        Assert.Equal(5, buttons.Count);
        Assert.Equal(new[] { "View Details", "View Profile",
            kind == ObligationKind.FiestaArawLotRental ? "Editing is unavailable: closed account or unsupported event-amount revision." : "Change approved amount",
            "Payment History", "Close Account" }, buttons.Select(b => b.GetAttribute("title")));
        Assert.Single(cut.FindComponents<RentalRegistryActionButtons>());
        Assert.Equal("Partial", cut.Find("td[data-label='Payment']").TextContent.Trim());
        Assert.Equal(kind == ObligationKind.FiestaArawLotRental, buttons[2].HasAttribute("disabled"));
        Assert.Empty(_stalls.Invocations);
        Assert.Empty(_payments.Invocations);

        cut.Find("button[title='View Details']").Click();
        Assert.Contains("not a receipt", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("Partial", cut.Find("td[data-label='Payment']").TextContent);
        cut.Find(".rph-footer button").Click();

        cut.Find("button[title='Close Account']").Click();
        Assert.Contains("Confirm account closure", cut.Markup);
        _accounts.Verify(x => x.CloseAccountAsync(It.IsAny<Guid>(), It.IsAny<CloseObligationAccountRequest>()), Times.Never);
    }

    [Theory]
    [InlineData("KM", ObligationKind.KanmanggaySpaceRental, "/operations/kanmanggay/manage-space-holders")]
    [InlineData("Vegetable/Fruits", ObligationKind.VegetableFruitSpaceRental, "/operations/vegetable-fruit/manage-space-holders")]
    [InlineData("FA", ObligationKind.FiestaArawLotRental, "/vendors?workspace=FA")]
    public void SpaceActionProfileAndHistoryRemainTypedToTheSelectedAccount(string code, ObligationKind kind, string route)
    {
        var account = Account(kind); Serve(account);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id))
            .ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?workspace={Uri.EscapeDataString(code)}");
        var cut = RenderComponent<Vendor>();

        cut.Find("button[title='Payment History']").Click();
        Assert.NotNull(cut.Find("[role='dialog'][aria-label='Payment history']"));
        cut.Find(".rph-footer button").Click();
        cut.Find("button[title='View Profile']").Click();
        var uri = Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains(route, Uri.UnescapeDataString(uri));
        Assert.Contains($"account={account.Id}", uri);
        Assert.Empty(_stalls.Invocations);
        Assert.Empty(_payments.Invocations);
    }

    [Theory]
    [InlineData("KM", ObligationKind.KanmanggaySpaceRental)]
    [InlineData("Vegetable/Fruits", ObligationKind.VegetableFruitSpaceRental)]
    public void SpaceEditActionOpensExistingApprovedRateDrawerWithoutWriting(string code, ObligationKind kind)
    {
        var account = Account(kind); Serve(account);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?workspace={Uri.EscapeDataString(code)}");
        var cut = RenderComponent<Vendor>();
        cut.Find("button[title='Change approved amount']").Click();
        Assert.Contains("Effective from (month)", cut.Find("[role='dialog']").TextContent);
        _accounts.Verify(x => x.SetRateAsync(It.IsAny<Guid>(), It.IsAny<SetObligationRateRequest>()), Times.Never);
        _accounts.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
    }

    [Theory]
    [InlineData("KM", ObligationKind.KanmanggaySpaceRental)]
    [InlineData("Vegetable/Fruits", ObligationKind.VegetableFruitSpaceRental)]
    [InlineData("FA", ObligationKind.FiestaArawLotRental)]
    public void ClosedSpaceAccountShowsFiveIconsButCannotReopenOrEdit(string code, ObligationKind kind)
    {
        var account = Account(kind, closed: true); Serve(account);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id))
            .ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo(
            $"/vendors?workspace={Uri.EscapeDataString(code)}&account={account.Id}");
        var cut = RenderComponent<Vendor>();
        var buttons = cut.FindAll(".registry-actions button");
        Assert.Equal(5, buttons.Count);
        Assert.True(buttons[2].HasAttribute("disabled"));
        Assert.True(buttons[4].HasAttribute("disabled"));
        Assert.Contains("Reopen unavailable", buttons[4].GetAttribute("title"));
        Assert.Empty(_stalls.Invocations);
        Assert.Empty(_payments.Invocations);
        _accounts.Verify(x => x.CloseAccountAsync(It.IsAny<Guid>(), It.IsAny<CloseObligationAccountRequest>()), Times.Never);
    }

    [Fact]
    public void FacilityRegistryConsumesTheSameRegistryTable()
    {
        var vendor = new VendorListItemDto(Guid.NewGuid(), "11", "Facility occupant", "Signed lessee", null, FacilityCode.NCC,
            "New Commercial Center", null, "Corner", null, null, 900m, StallStatus.Active,
            PaymentStatus.Partial, new(2026, 1, 1), 3, 12.5, "Corner");
        _vendors.Setup(x => x.GetVendorRegistryAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<VendorRegistryDto>.Success(new(1, 1, 0, 0, 0, 0, 0, 0m, 0m, [vendor])));
        _payments.Setup(x => x.GetPaymentHistoryAsync(vendor.StallId)).ReturnsAsync(Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Payments.PaymentHistoryDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vendors?facility=ncc");
        var cut = RenderComponent<Vendor>();
        cut.WaitForElement(".vs-toolbar-unified");
        Assert.NotNull(cut.FindComponent<RentalRegistryHero>());
        var bar = Assert.Single(cut.FindAll(".vs-toolbar-unified"));
        Assert.NotNull(bar.QuerySelector(".facility-selector"));
        Assert.NotNull(bar.QuerySelector(".filter-tabs-inline"));
        Assert.NotNull(bar.QuerySelector(".search-box"));
        Assert.NotNull(bar.QuerySelector(".vs-actions-compact"));
        Assert.Empty(cut.FindAll(".rental-picker"));
        cut.WaitForElement(".registry-table");
        Assert.Contains("Facility occupant", cut.Find("tbody").TextContent);
        Assert.Equal(new[] { "View Details", "View Profile", "Edit", "Payment History", "Close Stall" }, cut.FindAll(".registry-actions button").Select(x => x.GetAttribute("title")));
        cut.Find("button[title='View Details']").Click();
        Assert.Contains("Facility occupant", cut.Find("[role='dialog']").TextContent);
    }

    public sealed class FacilityFixture
    {
        public Guid StallGuid { get; set; } = Guid.NewGuid();
        public string StallNo { get; set; } = "ICE-17";
        public string ActualOccupant { get; set; } = "ICE holder";
        public string ContractName { get; set; } = "ICE contract name";
        public DateTime? ContractDate { get; set; } = new(2026, 1, 1);
        public decimal MonthlyRate { get; set; } = 900m;
        public bool IsPaid { get; set; }
    }
    [Fact]
    public void FacilityTableAndKmOverviewShareTheRealTableAndActionOrder()
    {
        var facilityRow = new FacilityFixture(); Guid? payment = null, history = null;
        var original = RenderComponent<FacilityStallsTable<FacilityFixture>>(p => p.Add(x => x.Facility, "ICE").Add(x => x.ColumnSet, "MonthlyTable")
            .Add(x => x.FilteredStalls, [facilityRow]).Add(x => x.OnPaymentClick, row => payment = row.StallGuid)
            .Add(x => x.OnHistoryClick, row => history = row.StallGuid));
        Serve(Account()); var km = RenderComponent<Kanmanggay>();
        Assert.NotNull(original.FindComponent<RegisteredRentalTable<FacilityFixture>>());
        Assert.NotNull(km.FindComponent<RegisteredRentalTable<ObligationAccountDto>>());
        Assert.Equal(original.FindAll("tbody button").Select(x => x.TextContent), km.FindAll("tbody button").Select(x => x.TextContent));
        Click(original, "₱"); Click(original, "↺"); Assert.Equal(facilityRow.StallGuid, payment); Assert.Equal(facilityRow.StallGuid, history);
        Click(original, "→"); Assert.Contains($"/profile/ice/{facilityRow.StallGuid}", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void ClosedRegisterIncludesOnlyGenuinelyClosedObligationsAndKeepsTotalsSeparate()
    {
        var km = Account(closed: true); var fa = Account(ObligationKind.FiestaArawLotRental, true);
        var openEvent = Account(ObligationKind.FiestaArawLotRental); var vf = Account(ObligationKind.VegetableFruitSpaceRental, true);
        Serve(km, fa, openEvent, vf);
        var cut = RenderComponent<ClosedAccounts>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".ca-obligation-source tr[data-account]").Count));
        Assert.Empty(cut.FindAll($"tr[data-account='{openEvent.Id}']"));
        Assert.Contains("Closed event lot", cut.Find($"tr[data-account='{fa.Id}']").TextContent);
        Assert.DoesNotContain(cut.FindAll(".ca-obligation-source button"), b => b.TextContent.Contains("Renew") || b.TextContent.Contains("Reopen"));
        Assert.Equal("₱0", cut.FindAll(".ca-sum-val")[1].TextContent);
        Assert.Equal("₱0", cut.FindAll(".ca-sum-val")[2].TextContent);
        Assert.Contains("not included", cut.Find(".ca-obligation-source .ca-source-note").TextContent);
        var csv = Uri.UnescapeDataString(cut.Find("a[download$='.csv']").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains(km.Id.ToString(), csv); Assert.Contains(fa.Id.ToString(), csv); Assert.Contains("Already in source account workspace", csv);
        Assert.DoesNotContain(openEvent.Id.ToString(), csv);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("closed-rental-register", cut.Markup);
        Click(cut, "KM"); Assert.Single(cut.FindAll(".ca-obligation-source tr[data-account]"));
        Click(cut, "Lapsed"); Assert.Empty(cut.FindAll(".ca-obligation-source tr[data-account]"));
        Click(cut, "All Status"); Click(cut, "Print"); Assert.Single(JSInterop.Invocations["print"]);
        Assert.DoesNotContain(_accounts.Invocations, x => x.Method.Name == nameof(IObligationsApiClient.CloseAccountAsync));
    }

    [Fact]
    public void ClosedSourceFailureIsVisibleAndCannotExportAnIncompleteRegister()
    {
        _accounts.Setup(x => x.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental)).ReturnsAsync(Result<ObligationWorkspaceDto>.Failure("Offline"));
        var cut = RenderComponent<ClosedAccounts>();
        cut.WaitForAssertion(() => Assert.Contains("KM: Offline", cut.Find("[role='alert']").TextContent));
        Assert.Empty(cut.FindAll("a[download]"));
        Assert.DoesNotContain("No inactive accounts", cut.Markup);
    }

    [Fact]
    public void UnknownRegistrySourceNeverFallsBackToAnNpmAccount()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vendors?workspace=UNKNOWN");
        var cut = RenderComponent<Vendor>();
        Assert.Contains("source is unavailable", cut.Find("[role='alert']").TextContent);
        Assert.Empty(_stalls.Invocations); Assert.Empty(_vendors.Invocations); Assert.Empty(_accounts.Invocations);
    }

    [Fact]
    public void ClosedRegisterCanPrintManySourceOwnedAccounts()
    {
        var template = Account(closed: true);
        Serve(Enumerable.Range(1, 90).Select(i => template with { Id = Guid.NewGuid(), SubjectLabel = i.ToString("000"), ActualOccupant = $"Audit holder {i:000}" }).ToArray());
        var cut = RenderComponent<ClosedAccounts>();
        cut.WaitForAssertion(() => Assert.Equal(90, cut.FindAll("tr[data-account]").Count));
        Assert.DoesNotContain(cut.FindAll(".ca-obligation-source button"), x => x.TextContent.Contains("Renew"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact("closed-rental-register-many", cut.Markup);
    }
}

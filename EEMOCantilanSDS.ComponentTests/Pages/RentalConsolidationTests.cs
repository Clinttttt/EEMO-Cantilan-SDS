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
        Services.AddSingleton(Mock.Of<ILifecycleApiClient>()); Services.AddSingleton(Mock.Of<ISetupApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
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

    private static void ChooseOption(IRenderedFragment cut, string caption, string label)
    {
        cut.FindAll(".fh-dd").Single(d => d.QuerySelector(".fh-dd-cap")?.TextContent.Trim() == caption).QuerySelector(".fh-dd-trigger")!.Click();
        cut.FindAll(".fh-dd-item").Single(i => i.TextContent.Trim() == label).Click();
    }

    [Theory]
    [InlineData("ICE")]
    [InlineData("NCC")]
    public void FacilityAndKmUseTheActualSameFormSections(string facility)
    {
        var form = new AddVendorModal.VendorModalForm { FacilityCode = facility, StallNo = "017", ActualOccupant = "Facility occupant", ContractName = "Signed name", ContractYears = 3, MonthlyRate = 900m };
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
        Assert.Contains("Approved Monthly Rental", km.Markup);
        Assert.Contains("Rent / Space Information", km.Markup);
        Assert.Contains("Additional Information", km.Markup);
        Assert.Contains("Space No.", km.FindComponent<RentalContractFields>().Markup);
        Assert.Empty(km.FindComponent<RentalUnitNumberField>().FindAll("input"));
        var duration = km.FindComponents<FhSelect>().Single(x => x.Instance.AriaLabel == "Contract Duration (No. of Years)");
        Assert.True(duration.Instance.Block);
        Assert.Equal("3", duration.Instance.Value);
        Assert.Equal(new[] { ("1", "1 year"), ("2", "2 years"), ("3", "3 years"), ("5", "5 years"), ("custom", "Custom…") }, duration.Instance.Options);
        Assert.Null(duration.Instance.DisabledValues);
        Assert.NotEmpty(original.FindComponents<FhSelect>());
        Assert.Empty(km.FindAll("#obw-contract-years"));
        Assert.Single(km.FindAll("input[type='date']"));
        Assert.Equal(form.StallNo, original.FindComponent<RentalUnitNumberField>().Find("input").GetAttribute("value"));
        Assert.Equal(PhilippineTime.Today.ToString("yyyy-MM-dd"), km.Find("#obw-contract-date").GetAttribute("value"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact(facility.ToLowerInvariant() + "-shared-registration", original.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("km-shared-registration", km.Markup);
        duration.Find("button").Click();
        Assert.Equal(5, duration.FindAll("[role='option']").Count);
        Assert.Empty(duration.FindAll(".fh-dd-disabled"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact("km-duration-open", km.Markup);
        duration.FindAll("[role='option']").Single(x => x.TextContent.Trim() == "1 year").Click();
        Assert.Equal("1", duration.Instance.Value);
        duration.Find("button").Click();
        duration.FindAll("[role='option']").Single(x => x.TextContent.Trim() == "Custom…").Click();
        km.Find("input[aria-label='Custom contract duration in years']").Input("7");
        Assert.Equal("7", km.Find("input[aria-label='Custom contract duration in years']").GetAttribute("value"));
        Assert.DoesNotContain("Recorded term does not automatically", km.Markup);
        var iceDuration = original.FindComponents<FhSelect>().Single(x => x.Instance.Options.Any(o => o.Value == "custom"));
        iceDuration.Find("button").Click();
        SpaceHolderWorkspaceTests.SaveVisualArtifact(facility.ToLowerInvariant() + "-duration-open", original.Markup);
    }

    [Theory]
    [InlineData("4", "Auto-suggested next Space No.")]
    [InlineData(null, "Assigned upon registration")]
    public void ServerNumberPreviewIsConciseAndNeverEditable(string? preview, string text)
    {
        var cut = RenderComponent<RentalUnitNumberField>(p => p.Add(x => x.ServerAssigned, true).Add(x => x.SuggestedNumber, preview));
        Assert.Contains(text, cut.Markup);
        Assert.Empty(cut.FindAll("input"));
        Assert.DoesNotContain("The server confirms", cut.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact(preview is null ? "number-preview-empty" : "number-preview", cut.Markup);
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
        // Contract reference was deliberately removed from the simplified Space form;
        // opening a new account must not synthesize the old reference.
        Assert.Empty(cut.FindAll("input[placeholder='As written on the contract']"));
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
        Assert.NotNull(request); Assert.Equal(1100.50m, request!.Amount); Assert.Null(request.ContractReference);
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
            new(EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year, EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Month, 1), 900m, 400m, 500m, Guid.Empty, "Same name", null, true);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([period]));
        var cut = RenderComponent<Kanmanggay>();
        Assert.NotNull(cut.FindComponent<RegisteredRentalTable<ObligationAccountDto>>());
        Assert.NotNull(cut.FindComponent<RentalSummaryStrip>());
        Assert.Equal(new[] { "₱", "↺", "→" }, cut.FindAll("tbody button").Select(x => x.TextContent));
        Click(cut, "₱");
        var receipt = cut.Find(".receipt").TextContent;                                    // the stall registers' own acknowledgement, read from the account's period register
        Assert.Contains("Monthly Rental", receipt); Assert.Contains("₱900.00", receipt); Assert.Contains("Balance Due", receipt); Assert.Contains("₱500.00", receipt); Assert.Contains("Total Paid", receipt); Assert.Contains("₱400.00", receipt);
        _accounts.Verify(x => x.GetRegisterAsync(account.Id), Times.Once);
        Assert.Empty(_activity.Invocations); Assert.Empty(_payments.Invocations);
        Click(cut, "Close"); Click(cut, "→");
        Assert.EndsWith($"/operations/kanmanggay/profile/{account.Id}", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.DoesNotContain(_accounts.Invocations, x => x.Method.Name == nameof(IObligationsApiClient.CreateAccountAsync));
    }

    [Fact]
    public void HistoryMatchesPeriodIdsNotNamesAndPreservesSrcCorrectionEvidence()
    {
        var account = Account(); Serve(account); var periodId = Guid.NewGuid();
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([
            new(account.Id, periodId, account.Kind, account.KindLabel, account.SubjectLabel, new(2026, 9, 1), 900m, 400m, 500m, Guid.Empty, "Same name", null, true)]));
        // The account's own canonical payments (read by THIS account's id on the server), one reversed and replaced, one live.
        var correction = new ObligationPaymentCorrectionDto("Reversal", "Authorized correction", PhilippineTime.Today, DateTime.UtcNow, -400m);
        var reversed = new ObligationPaymentDto(Guid.NewGuid(), "SRC-CORRECT", PhilippineTime.Today, DateTime.UtcNow.AddMinutes(-5), new(2026, 9, 1), 400m, -400m, 0m,
            RevenueInstrumentType.OfficialReceipt, "Pat Collector", "Reversed", [correction], ["SRC-REPLACEMENT"]);
        var live = new ObligationPaymentDto(Guid.NewGuid(), "SRC-REPLACEMENT", PhilippineTime.Today, DateTime.UtcNow, new(2026, 9, 1), 400m, 0m, 400m,
            RevenueInstrumentType.OfficialReceipt, "Pat Collector", "Posted");
        _accounts.Setup(x => x.GetPaymentsAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationPaymentDto>>.Success([reversed, live]));
        var cut = RenderComponent<Kanmanggay>(); Click(cut, "↺");
        var dialog = cut.Find("[role='dialog'][aria-label='Payment history']").TextContent;
        Assert.Contains("SRC-CORRECT", dialog);                         // the original SRC is never hidden by its reversal
        Assert.Contains("Authorized correction", dialog);
        Assert.Contains("Replacement: SRC-REPLACEMENT", dialog);
        Assert.Contains("Pat Collector", dialog);
        _accounts.Verify(x => x.GetPaymentsAsync(account.Id), Times.Once);       // keyed by the account's id, never a name
        Assert.Empty(_activity.Invocations);                                      // no second, name-matched feed
        Assert.Empty(_payments.Invocations);
        // The ledger row for the period shows the live payment, not the reversed one.
        Assert.Contains(cut.FindAll(".ph-ledger-row .ph-or-num"), x => x.TextContent.Trim() == "SRC-REPLACEMENT");
        Assert.DoesNotContain(cut.FindAll(".ph-ledger-row .ph-or-num"), x => x.TextContent.Contains("SRC-CORRECT"));
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
        Assert.Single(cut.FindAll(".vs-toolbar-unified"));
        Assert.Single(cut.FindComponents<RentalRegistryToolbar>());
        Assert.NotNull(cut.Find(".vs-toolbar-unified .search-input"));
        Assert.Empty(cut.FindAll(".obw-layout > section > .v3-panel-header"));
        Assert.Empty(cut.FindAll(".obw-layout .obw-actions-bar"));
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
            kind == ObligationKind.FiestaArawLotRental ? "Editing is unavailable: closed account or unsupported event-amount revision." : "Edit Space Holder",
            "Payment History", "Close Account" }, buttons.Select(b => b.GetAttribute("title")));
        Assert.Single(cut.FindComponents<RentalRegistryActionButtons>());
        Assert.Equal("Partial", cut.Find("td[data-label='Payment']").TextContent.Trim());
        Assert.Equal(kind == ObligationKind.FiestaArawLotRental, buttons[2].HasAttribute("disabled"));
        Assert.Empty(_stalls.Invocations);
        Assert.Empty(_payments.Invocations);

        cut.Find("button[title='View Details']").Click();
        var detailsText = cut.Find("[role='dialog']").TextContent;
        Assert.Contains(kind == ObligationKind.FiestaArawLotRental ? "Lot holder & event" : "Occupant & contract", detailsText);
        Assert.Contains("Fee structure", detailsText);
        Assert.DoesNotContain(account.Id.ToString(), detailsText);
        Assert.Contains("Source occupant", detailsText);
        if (kind == ObligationKind.FiestaArawLotRental)
        {
            Assert.DoesNotContain("Contract effective date", detailsText);
            Assert.DoesNotContain("Area (sq.m)", detailsText);
        }
        Assert.Contains("Partial", cut.Find("td[data-label='Payment']").TextContent);
        cut.FindAll(".dv-footer button").Single(b => b.TextContent == "History").Click();
        Assert.Contains(kind == ObligationKind.FiestaArawLotRental ? "Event Settlement Record" : "12-Month Payment Record", cut.Find("[role='dialog']").TextContent);   // the stall history's own heading
        cut.Find(".ph-footer button").Click();

        cut.Find("button[title='Close Account']").Click();
        Assert.Contains("Confirm account closure", cut.Markup);
        _accounts.Verify(x => x.CloseAccountAsync(It.IsAny<Guid>(), It.IsAny<CloseObligationAccountRequest>()), Times.Never);
    }

    [Theory]
    [InlineData("KM", ObligationKind.KanmanggaySpaceRental, "/operations/kanmanggay/profile")]
    [InlineData("Vegetable/Fruits", ObligationKind.VegetableFruitSpaceRental, "/operations/vegetable-fruit/profile")]
    [InlineData("FA", ObligationKind.FiestaArawLotRental, "/operations/fiesta-araw/profile")]
    public void SpaceActionProfileAndHistoryRemainTypedToTheSelectedAccount(string code, ObligationKind kind, string route)
    {
        var account = Account(kind); Serve(account);
        _accounts.Setup(x => x.GetRegisterAsync(account.Id))
            .ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?workspace={Uri.EscapeDataString(code)}");
        var cut = RenderComponent<Vendor>();

        cut.Find("button[title='Payment History']").Click();
        Assert.NotNull(cut.Find("[role='dialog'][aria-label='Payment history']"));
        cut.Find(".ph-footer button").Click();
        cut.Find("button[title='View Profile']").Click();
        var uri = Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains(route, Uri.UnescapeDataString(uri));
        Assert.Contains($"{account.Id}", uri);
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
        cut.Find("button[title='Edit Space Holder']").Click();
        Assert.Contains("Lessee Information", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("Space No.", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("007", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("Change approved amount", cut.Find("[role='dialog']").TextContent);
        _accounts.Verify(x => x.CorrectHolderAsync(It.IsAny<Guid>(), It.IsAny<CorrectObligationHolderRequest>()), Times.Never);
        _accounts.Verify(x => x.SetRateAsync(It.IsAny<Guid>(), It.IsAny<SetObligationRateRequest>()), Times.Never);
        _accounts.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
    }

    [Fact]
    public void SpaceEditorSubmitsAuditedSourceHolderEvidence_WithoutPostingMoney()
    {
        var account = Account(); Serve(account);
        CorrectObligationHolderRequest? sent = null;
        _accounts.Setup(x => x.CorrectHolderAsync(account.Id, It.IsAny<CorrectObligationHolderRequest>()))
            .Callback<Guid, CorrectObligationHolderRequest>((_, request) => sent = request)
            .ReturnsAsync(Result<bool>.Failure("Review contract evidence"));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vendors?workspace=KM");
        var cut = RenderComponent<Vendor>();
        cut.Find("button[title='Edit Space Holder']").Click();
        cut.Find("#obw-occupant").Input("Corrected source occupant");
        cut.Find("input[placeholder='As written on the signed contract']").Input("Corrected signed lessee");
        // The reference is no longer editable in this drawer, but existing evidence
        // must survive an unrelated holder/area correction unchanged.
        Assert.Empty(cut.FindAll("input[placeholder='As written on the contract']"));
        cut.Find("#obw-area-note").Change("Corner A");
        cut.Find("form[aria-label='Edit Occupant & Space']").Submit();
        cut.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal("Corrected source occupant", sent!.ActualOccupant);
        Assert.Equal("Corrected signed lessee", sent.ContractName);
        Assert.Equal(account.ContractReference, sent.ContractReference);
        Assert.Equal("Corner A", sent.AreaLocation);
        Assert.Contains("Review contract evidence", cut.Find("[role='dialog']").TextContent);
        _accounts.Verify(x => x.SetRateAsync(It.IsAny<Guid>(), It.IsAny<SetObligationRateRequest>()), Times.Never);
        _accounts.Verify(x => x.CloseAccountAsync(It.IsAny<Guid>(), It.IsAny<CloseObligationAccountRequest>()), Times.Never);
        Assert.Empty(_payments.Invocations);
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
        Assert.Contains("Open the account profile to resume, renew, reassign or retire", buttons[4].GetAttribute("title"));
        Assert.Empty(_stalls.Invocations);
        Assert.Empty(_payments.Invocations);
        _accounts.Verify(x => x.CloseAccountAsync(It.IsAny<Guid>(), It.IsAny<CloseObligationAccountRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(FacilityCode.NPM, "npm")]
    [InlineData(FacilityCode.NCC, "ncc")]
    [InlineData(FacilityCode.TCC, "tcc")]
    [InlineData(FacilityCode.ICE, "ice")]
    [InlineData(FacilityCode.BBQ, "bbq")]
    public void FacilityRegistryConsumesTheSameRegistryTable(FacilityCode facility, string slug)
    {
        var vendor = new VendorListItemDto(Guid.NewGuid(), "11", "Facility occupant", "Signed lessee", null, facility,
            "New Commercial Center", null, "Corner", null, null, 900m, StallStatus.Active,
            PaymentStatus.Partial, new(2026, 1, 1), 3, 12.5, "Corner");
        _vendors.Setup(x => x.GetVendorRegistryAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<VendorRegistryDto>.Success(new(1, 1, 0, 0, 0, 0, 0, 0m, 0m, [vendor])));
        _payments.Setup(x => x.GetPaymentHistoryAsync(vendor.StallId)).ReturnsAsync(Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Payments.PaymentHistoryDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?facility={slug}");
        var cut = RenderComponent<Vendor>();
        cut.WaitForElement(".vs-toolbar-unified");
        Assert.NotNull(cut.FindComponent<RentalRegistryHero>());
        var bar = Assert.Single(cut.FindAll(".vs-toolbar-unified"));
        Assert.Single(cut.FindComponents<RentalRegistryToolbar>());
        Assert.NotNull(bar.QuerySelector(".facility-selector"));
        Assert.NotNull(bar.QuerySelector(".filter-tabs-inline"));
        Assert.NotNull(bar.QuerySelector(".search-box"));
        Assert.NotNull(bar.QuerySelector(".vs-actions-compact"));
        Assert.Empty(cut.FindAll(".rental-picker"));
        cut.WaitForElement(".registry-table");
        Assert.Contains("Facility occupant", cut.Find("tbody").TextContent);
        cut.FindComponent<RentalRegistryToolbar>().InvokeAsync(() =>
            cut.FindComponent<RentalRegistryToolbar>().Instance.OnSelected.InvokeAsync("ICE"));
        Assert.EndsWith("/vendors?facility=ice", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal("ICE", cut.FindComponent<RentalRegistryToolbar>().Instance.Selected);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vendors?facility=npm");
        Assert.Equal("NPM", cut.FindComponent<RentalRegistryToolbar>().Instance.Selected);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vendors?facility={slug}");
        Assert.Equal(new[] { "View Details", "View Profile", "Edit", "Payment History", "Close Stall" }, cut.FindAll(".registry-actions button").Select(x => x.GetAttribute("title")));
        SpaceHolderWorkspaceTests.SaveVisualArtifact(slug + "-registry", cut.Markup);
        cut.Find("button[title='View Details']").Click();
        Assert.Contains("Facility occupant", cut.Find("[role='dialog']").TextContent);
    }

    [Fact]
    public void KmRegistryUsesTheOriginalFacilityToolbarStructure()
    {
        Serve(Account());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vendors?workspace=KM");
        var cut = RenderComponent<Vendor>();
        var bar = Assert.Single(cut.FindAll(".vs-toolbar-unified"));
        Assert.Equal(new[] { "facility-selector", "filter-tabs-inline", "search-box", "vs-actions-compact" },
            bar.Children.Select(x => x.ClassList.First()));
        Assert.Equal(new[] { "All", "Paid", "Partial", "Unpaid", "Closed" },
            bar.QuerySelectorAll(".filter-tab").Select(x => x.TextContent.Trim()));
        Assert.DoesNotContain("Space Holder List", bar.TextContent);
        Assert.DoesNotContain("Reports", bar.TextContent);
        Assert.Single(bar.QuerySelectorAll("details:not([open])"));
        Assert.Empty(cut.FindAll(".obw-layout > section > .v3-panel-header"));
        Assert.Empty(cut.FindAll(".obw-layout .obw-actions-bar"));
        Assert.Contains("Add Occupant", bar.TextContent);
        Assert.Single(cut.FindComponents<RentalRegistryToolbar>());
        cut.Find("#rental-workspace-picker").Click();
        Assert.Equal("true", cut.Find("#rental-workspace-picker").GetAttribute("aria-expanded"));
        cut.Find(".facility-selector").KeyDown("Escape");
        Assert.Empty(cut.FindAll(".fac-dropdown"));
        cut.Find(".filter-tab.active").Click();
        cut.Find(".search-input").Input("No match");
        cut.Find("button[aria-label='Clear search']").Click();
        Assert.Equal(string.Empty, cut.Find(".search-input").GetAttribute("value"));
    }

    [Fact]
    public void PrimaryStatusReturnsToActiveAccountsWhileAdvancedLifecycleRetainsClosedPaid()
    {
        var active = Account() with { SubjectLabel = "A", OutstandingToDate = 0m };
        var closed = Account(closed: true) with { SubjectLabel = "B", OutstandingToDate = 0m };
        Serve(active, closed);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vendors?workspace=KM");
        var cut = RenderComponent<Vendor>();
        Click(cut, "Closed");
        Assert.Contains("B", cut.Find("tbody").TextContent);
        Click(cut, "Paid");
        Assert.Contains("A", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("B", cut.Find("tbody").TextContent);
        cut.Find("[aria-label='Account lifecycle']").Change("Closed");
        Click(cut, "Paid");
        Assert.Contains("B", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("A", cut.Find("tbody").TextContent);
        Assert.Equal("1 spaces", cut.Find(".vs-count-label").TextContent.Trim());
        Assert.Empty(cut.FindAll(".obw-layout > section > .v3-panel-header"));
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
        Assert.Contains("not part of the totals", cut.Find(".ca-obligation-source .ca-source-note").TextContent);
        var csv = Uri.UnescapeDataString(cut.Find("a[download$='.csv']").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains(km.Id.ToString(), csv); Assert.Contains(fa.Id.ToString(), csv); Assert.Contains("Already in source account workspace", csv);
        Assert.DoesNotContain(openEvent.Id.ToString(), csv);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("closed-rental-register", cut.Markup);
        // Source and Status are compact selectors now; the behavior they drive is unchanged.
        ChooseOption(cut, "Source", "Kanmanggay"); Assert.Single(cut.FindAll(".ca-obligation-source tr[data-account]"));
        ChooseOption(cut, "Status", "Lapsed"); Assert.Empty(cut.FindAll(".ca-obligation-source tr[data-account]"));
        ChooseOption(cut, "Status", "All statuses"); Click(cut, "Print"); Assert.Single(JSInterop.Invocations["print"]);
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
        cut.WaitForAssertion(() => Assert.Equal(90, cut.FindAll("tr[data-account]").Count), TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(cut.FindAll(".ca-obligation-source button"), x => x.TextContent.Contains("Renew"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact("closed-rental-register-many", cut.Markup);
    }
}

using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// "Renew expired terms" for monthly spaces: lists only the spaces whose term ran out AND that the server offers Renew on, and renews each through the
/// same previewed, versioned lifecycle action its profile uses — one operation per account, with the corrected contract name when one was typed.
/// </summary>
public sealed class SpaceTermRenewalsTests : TestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly Mock<IObligationsApiClient> _accounts = new();
    private readonly Mock<ILifecycleApiClient> _lifecycle = new();
    private readonly List<(Guid Id, LifecycleExecution Execution)> _executed = [];

    public SpaceTermRenewalsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Admin").SetRoles("Admin");
        Services.AddSingleton(_accounts.Object);
        Services.AddSingleton(_lifecycle.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>(); Services.AddSingleton<FacilityState>(new FacilityState(Mock.Of<IFacilitiesApiClient>()));
        _lifecycle.Setup(x => x.PreviewAsync(LifecycleSource.SpaceRental, It.IsAny<Guid>(), It.IsAny<LifecycleCommand>()))
            .ReturnsAsync((LifecycleSource _, Guid id, LifecycleCommand c) => Result<LifecyclePreviewDto>.Success(new(c.Action, true, null, null, $"v-{id}", new Dictionary<string, string>())));
        _lifecycle.Setup(x => x.ExecuteAsync(LifecycleSource.SpaceRental, It.IsAny<Guid>(), It.IsAny<LifecycleExecution>()))
            .ReturnsAsync((LifecycleSource _, Guid id, LifecycleExecution e) => { _executed.Add((id, e)); return Result<LifecycleOutcome>.Success(new(Guid.NewGuid(), id, null, e.Command.Action, false)); });
    }

    private static ObligationAccountDto Space(string holder, string no, DateOnly signed, int years = 1) => new(
        Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental, "Kanmanggay", Guid.NewGuid(), holder, null, null, no, null, null, signed, null, 900m, signed,
        1800m, 0m, 1800m, OccupancyArrangement.SignedContract, $"KM-{no}", holder, holder, signed, years);

    private static AccountLifecycleDto Life(Guid id, bool renewable) => new(LifecycleSource.SpaceRental, id, "", "KanmanggaySpaceRental", null, default, null, null, null,
        [new(LifecycleAction.Renew, renewable, renewable ? null : "TermStillRunning", null, "Admin", true)], []);

    [Fact]
    public void ListsOnlyEndedTermsTheServerOffersRenewOn_AndRenewsEachThroughItsOwnPreviewedOperation_WithTheCorrectedName()
    {
        var ended = Space("Lorenofs Brando", "5", new DateOnly(2022, 10, 25));
        var refused = Space("Ana Reyes", "6", new DateOnly(2022, 10, 25));
        var running = Space("Diosa Cortes", "7", PhilippineTime.Today.AddMonths(-2), years: 3);
        _accounts.Setup(x => x.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([ended, refused, running]));
        _lifecycle.Setup(x => x.GetManyAsync(LifecycleSource.SpaceRental, It.IsAny<IReadOnlyList<Guid>>()))
            .ReturnsAsync(() => Result<IReadOnlyList<AccountLifecycleDto>>.Success([Life(ended.Id, _executed.Count == 0), Life(refused.Id, false)]));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/operations/kanmanggay/renewals");

        var cut = RenderComponent<SpaceTermRenewals>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".str-table tbody tr")), Wait);
        Assert.Contains("Lorenofs Brando", cut.Find(".str-table").TextContent);
        Assert.True(cut.Find(".str-form button").HasAttribute("disabled"));                    // a reason is required first

        cut.Find(".str-pencil").Click();
        cut.Find("#spr-edit-contract").Input("Lorenofs B. Brando");
        cut.FindAll(".str-edit-actions button")[0].Click();
        cut.Find("#spr-reason").Input("Renewal approved, Res. 12");
        cut.Find("#spr-years").Change("3");
        cut.Find(".str-form").Submit();

        cut.WaitForAssertion(() => Assert.Single(_executed), Wait);
        var (id, execution) = _executed[0];
        Assert.Equal(ended.Id, id);
        Assert.Equal($"v-{ended.Id}", execution.ExpectedVersion);                             // executed at the version it was previewed at
        Assert.Equal((LifecycleAction.Renew, 3, "Lorenofs B. Brando", "KM-5", "Renewal approved, Res. 12"),
            (execution.Command.Action, execution.Command.TermYears, execution.Command.ContractName, execution.Command.Reference, execution.Command.Reason));
        cut.WaitForAssertion(() => Assert.Contains("Nothing to renew", cut.Markup), Wait);   // renewed: it drops off
    }
}

using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Monthly Income lines with no StallTrack writer (Vegetable / Fruits, Transfer Large Cattle, Kanmanggay, Fiesta / Araw,
/// Fines) get a focused workspace that states the line is not recorded yet. They hold no entry form, sample rows or
/// collector / payor / accountable-form management.
/// </summary>
public sealed class UnrecordedOperationWorkspacesTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IRevenueClassificationsApiClient> _classifications = new();

    public UnrecordedOperationWorkspacesTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        _classifications.Setup(x => x.GetClassificationsAsync(It.IsAny<DateOnly?>())).ReturnsAsync(
            Result<IReadOnlyList<RevenueClassificationDto>>.Success(new[]
            {
                new RevenueClassificationDto(Guid.NewGuid(), RevenueClassificationCodes.PenaltiesAndFines, true, true,
                    new RevenueClassificationPolicyDto(Guid.NewGuid(), new DateOnly(2026, 1, 1), "Penalties/Fines", null,
                        RevenueInstrumentType.OfficialReceipt, DateTime.UtcNow, "seed")),
            }));
        Services.AddSingleton(_classifications.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(typeof(VegetableFruit), "/operations/vegetable-fruit", "Vegetable / Fruits")]
    [InlineData(typeof(TransferLargeCattle), "/operations/transfer-large-cattle", "Transfer Large Cattle")]
    [InlineData(typeof(Kanmanggay), "/operations/kanmanggay", "Kanmanggay")]
    [InlineData(typeof(FiestaAraw), "/operations/fiesta-araw", "Lot Rental — Fiesta / Araw")]
    [InlineData(typeof(Fines), "/operations/fines", "Fines")]
    public void Workspace_IsOfficeOnly_StatesNotRecorded_AndRecordsNothing(Type page, string route, string title)
    {
        var template = Assert.Single(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template;
        var roles = Assert.Single(page.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles;
        Assert.Equal(route, template);
        Assert.Equal("SuperAdmin,Admin", roles);

        var cut = Render(builder =>
        {
            builder.OpenComponent(0, page);
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(title, Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            Assert.Contains("aren't recorded in StallTrack yet", cut.Find("[role='status']").TextContent);
            Assert.Empty(cut.FindAll("form"));
            Assert.Empty(cut.FindAll("input"));
            Assert.Empty(cut.FindAll("table"));
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record") || b.TextContent.Contains("Collect"));
            Assert.DoesNotContain("₱", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void VegetableFruit_ShowsBothReceiptModes_WithoutASinglePolicyRow()
    {
        var cut = RenderComponent<VegetableFruit>();

        cut.WaitForAssertion(() =>
        {
            var facts = cut.Find("dl.v3-facts").TextContent;
            Assert.Contains("Whole payment", facts);
            Assert.Contains("Official Receipt", facts);
            Assert.Contains("Daily transaction", facts);
            Assert.Contains("Cash Ticket", facts);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/collectors");
        }, Timeout);

        // The classification reader returns one default policy; it would show a single instrument for a two-mode line.
        _classifications.Verify(x => x.GetClassificationsAsync(It.IsAny<DateOnly?>()), Times.Never);
    }

    [Fact]
    public void TransferLargeCattle_ClaimsNoInstrument()
    {
        var cut = RenderComponent<TransferLargeCattle>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No revenue classification, fee schedule or receipt type is set", cut.Markup);
            Assert.DoesNotContain("Official Receipt", cut.Markup);
            Assert.DoesNotContain("Cash Ticket", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void Fines_ReadsItsPolicyFromTheClassification()
    {
        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Penalties/Fines", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href") == "/collectors");
        }, Timeout);
    }
}

using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Head has the last word on an automatic late penalty (IA-072). A fine an officer assessed by hand stays as it was assessed.</summary>
public sealed partial class FineAssessmentComponentTests
{
    private IRenderedComponent<FineAssessmentsRegister> OpenDetails(FineAssessmentDto row)
    {
        _api.Setup(x => x.GetAssessmentsAsync(null, null, It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([row]));
        var cut = RenderComponent<FineAssessmentsRegister>();
        cut.WaitForAssertion(() => Assert.Contains("View details", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains("View details")).Click();
        return cut;
    }

    [Fact]
    public void TheHead_UpdatesAnAutomaticPenalty_WithAReason_AgainstTheCopyTheySaw()
    {
        var row = Row() with { CanAdjust = true };
        AdjustFineAssessmentRequest? sent = null;
        _api.Setup(x => x.AdjustAssessmentAsync(row.Id, It.IsAny<AdjustFineAssessmentRequest>())).Callback((Guid _, AdjustFineAssessmentRequest r) => sent = r)
            .ReturnsAsync(Result<FineAssessmentDto>.Success(row with { Assessed = 50m, Remaining = 50m, Version = 2 }));
        var cut = OpenDetails(row);

        cut.WaitForAssertion(() => Assert.Contains("Update this penalty", cut.Markup));
        cut.Find(".far-adjust").Submit();
        Assert.Contains("Enter the new amount and the reason.", cut.Markup);   // a reason is required, so nothing was sent
        Assert.Null(sent);

        cut.FindAll(".far-adjust input")[0].Change("50");
        cut.FindAll(".far-adjust input")[1].Change("Reviewed with lessee");
        cut.Find(".far-adjust").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal(new AdjustFineAssessmentRequest(50m, "Reviewed with lessee", 1), sent);
    }

    [Fact]
    public void ARefusedUpdate_IsStated()
    {
        var row = Row() with { CanAdjust = true };
        _api.Setup(x => x.AdjustAssessmentAsync(row.Id, It.IsAny<AdjustFineAssessmentRequest>())).ReturnsAsync(Result<FineAssessmentDto>.Failure("Reload this penalty and try again."));
        var cut = OpenDetails(row);
        cut.WaitForAssertion(() => Assert.Contains("Update this penalty", cut.Markup));

        cut.FindAll(".far-adjust input")[1].Change("Reviewed");
        cut.Find(".far-adjust").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Reload this penalty and try again.", cut.Markup));
    }

    [Fact]
    public void AnAdmin_SeesThePenalty_ButIsNotOfferedTheUpdate()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");

        var cut = OpenDetails(Row() with { CanAdjust = true });

        cut.WaitForAssertion(() => Assert.Contains("Fine assessment", cut.Markup));
        Assert.DoesNotContain("Update this penalty", cut.Markup);
    }

    [Fact]
    public void AManuallyAssessedFine_OffersNoUpdate()
    {
        var cut = OpenDetails(Row());

        cut.WaitForAssertion(() => Assert.Contains("Original assessment evidence is preserved.", cut.Markup));
        Assert.DoesNotContain("Update this penalty", cut.Markup);
    }
}

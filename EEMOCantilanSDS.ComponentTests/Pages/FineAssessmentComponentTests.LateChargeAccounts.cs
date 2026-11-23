using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed partial class FineAssessmentComponentTests
{
    [Fact]
    public void TheRegister_ListsLateChargesOncePerAccount_AboveTheOtherFines_AndLinksToTheAccount()
    {
        var stall = Guid.NewGuid();
        _api.Setup(x => x.GetAssessmentsAsync(null, null, It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([Row()]));
        _api.Setup(x => x.GetLateChargesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<LateChargeAccountDto>>.Success(
        [
            new(new(FineAccountKind.FacilityContract, Guid.NewGuid()), "Felix Santos", "Demo Trade Center", "D-08", 11, 13_200m, 7_920m, 0m, 21_120m, stall, FacilityCode.TCC),
            new(new(FineAccountKind.FacilityContract, Guid.NewGuid()), "Rosa Mendoza", "Demo Trade Center", "D-02", 1, 200m, 16m, 216m, 0m, Guid.NewGuid(), FacilityCode.TCC),
        ]));

        var cut = RenderComponent<FineAssessmentsRegister>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".far-late tbody tr").Count));
        var felix = cut.Find(".far-late tbody tr").TextContent;
        Assert.Contains("Felix Santos", felix);
        Assert.Contains("₱13,200.00", felix);
        Assert.Contains("₱7,920.00", felix);
        Assert.Contains("₱21,120.00", felix);
        Assert.Equal($"/profile/tcc/{stall}", cut.Find(".far-late a").GetAttribute("href"));
        Assert.Contains("Recorded incident", cut.Markup);   // the other fine is still listed on its own

        cut.Find("select").Change("Outstanding");
        Assert.Single(cut.FindAll(".far-late tbody tr"));
    }

    [Fact]
    public void LateChargesThatCannotBeRead_SaySo_WithoutHidingTheOtherFines()
    {
        _api.Setup(x => x.GetAssessmentsAsync(null, null, It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([Row()]));
        _api.Setup(x => x.GetLateChargesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<LateChargeAccountDto>>.Failure("Late charges could not be loaded."));

        var cut = RenderComponent<FineAssessmentsRegister>();

        cut.WaitForAssertion(() => Assert.Contains("Late charges could not be loaded.", cut.Markup));
        Assert.Contains("Recorded incident", cut.Markup);
    }
}

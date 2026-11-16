using System.Reflection;
using EEMOCantilanSDS.Application.Dtos.Tenancy;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// A plain /login on a deployment whose default office is not Cantilan (the demo office, a new deployment) showed Cantilan's seal
/// and "MEEDO", because the page's defaults are Cantilan's. It now brands as the deployment's own default office. Cantilan stays
/// the fallback (the office's instruction, 2026-08-16), so where Cantilan is the default nothing changes.
/// </summary>
public class LoginDefaultOfficeTests
{
    private static string? Brand(params MunicipalityDto[]? offices) =>
        (string?)typeof(EEMOCantilanSDS.Client.Components.Pages.Login)
            .GetMethod("DefaultOfficeToBrand", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [offices]);

    private static MunicipalityDto Office(string code, bool isDefault) => new(code, code, "Province", "Office", "Active", true, isDefault);

    [Fact]
    public void A_deployment_whose_default_office_is_not_Cantilan_is_branded_as_that_office() =>
        Assert.Equal("DEMO", Brand(Office("CANTILAN", false), Office("DEMO", true)));

    [Fact]
    public void Where_Cantilan_is_the_default_the_page_keeps_its_own_defaults() =>
        Assert.Null(Brand(Office("CANTILAN", true), Office("CARRASCAL", false)));

    [Fact]
    public void With_no_default_office_or_no_list_it_keeps_the_defaults()
    {
        Assert.Null(Brand(Office("CARRASCAL", false)));
        Assert.Null(Brand(null));
    }
}

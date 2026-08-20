using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Services;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// How the portal decides it is serving the platform's DEFAULT municipality.
///
/// <para>
/// Found by audit rather than reported. It compared the branding record's CODE to the literal "CANTILAN" - a tenant code
/// deciding behaviour, which is the pattern this system does not allow, and the same one that put another municipality's
/// facility names on Madrid's reports. Here it decided whose backdrop, seal, acronym and office name a page showed.
/// </para>
///
/// <para>
/// The municipality row already carries IsDefault, and now so does its branding projection, so the answer is read rather
/// than inferred from a name that could change.
/// </para>
/// </summary>
public class BrandingDefaultTenantTests
{
    private static BrandingState With(string code, bool isDefault)
    {
        var state = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());
        state.Apply(new MunicipalityBrandingDto(
            Code: code, TenantCode: code.ToLowerInvariant(), Name: code, Province: "Surigao del Sur",
            OfficeName: "Office", SealPath: null, Status: "Active", IsActive: true,
            OfficeAcronym: null, Address: null, ReportSignatories: null, IsDefault: isDefault));

        return state;
    }

    [Fact]
    public void TheDefaultMunicipalityIsRecognisedFromItsRecord()
    {
        Assert.True(With("CANTILAN", isDefault: true).IsDefaultTenant);
    }

    [Fact]
    public void ANONDefaultLGUIsNotTreatedAsTheDefaultEvenWhenItsCodeSuggestsNothing()
    {
        Assert.False(With("MADRID", isDefault: false).IsDefaultTenant);
    }

    [Fact]
    public void THECODEAloneNoLongerDecidesIt()
    {
        // The load-bearing case: a record whose code reads CANTILAN but which is NOT flagged default must not be treated as
        // the default. Under the old comparison it was - and that comparison was the only thing deciding.
        Assert.False(With("CANTILAN", isDefault: false).IsDefaultTenant);

        // And the reverse: the flag is believed even when the code is something else entirely.
        Assert.True(With("MADRID", isDefault: true).IsDefaultTenant);
    }

    [Fact]
    public void BeforeBrandingLoadsItStillAnswersTheWayItAlwaysDid()
    {
        // Deliberately unchanged. Nothing is known yet, and the default LGU is the one whose pages would otherwise flicker;
        // asserting it so a later change to this line is a decision rather than an accident.
        var state = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());

        Assert.True(state.IsDefaultTenant);
    }
}

using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Which facilities are collected AT THE POINT OF SERVICE, and therefore carry no part payment and no arrears.
/// </summary>
/// <remarks>
/// The office reported the collector's Reports screen offering "Part paid", "Not collected" and "Still owed" for Tabo-an, where a
/// vendor is paid the moment they are recorded — figures that can only ever read nought, and which read as "the office is owed
/// nothing" rather than "the question does not apply". The rule is stated once here because two screens now depend on it: the
/// financial report's PaidOnService rows and the collector's app.
///
/// <para>Pinned as a PARTITION rather than a list of three, so a facility added later must be classified deliberately. A new
/// facility defaults to not-paid-on-service, which is the safe direction: it would show arrears figures that are merely empty,
/// rather than hide arrears that are real.</para>
/// </remarks>
public class PaidOnServiceFacilityTests
{
    [Theory]
    [InlineData(FacilityCode.SLH)]   // a slaughter is paid when the animal is slaughtered
    [InlineData(FacilityCode.TRM)]   // a trip is paid when the trip is made
    [InlineData(FacilityCode.TPM)]   // a Tabo-an vendor is paid when they take their space
    public void TheseAreCollectedAtThePointOfService(FacilityCode facility) =>
        Assert.True(DomainRules.IsPaidOnService(facility));

    [Theory]
    [InlineData(FacilityCode.NPM)]   // a market day can be left owing
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.NCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    [InlineData(FacilityCode.Custom1)]
    public void TheseCanCarryABalance(FacilityCode facility) =>
        Assert.False(DomainRules.IsPaidOnService(facility));

    /// <summary>Every facility the office can hold is classified, so none is silently neither.</summary>
    [Fact]
    public void EveryFacilityCodeIsAnsweredFor()
    {
        var codes = Enum.GetValues<FacilityCode>();

        var paidOnService = codes.Where(DomainRules.IsPaidOnService).ToList();

        Assert.Equal(3, paidOnService.Count);
        Assert.Equal(new[] { FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM }, paidOnService);
    }
}

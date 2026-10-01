using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.UnitTest.Mobile;

/// <summary>
/// The unbound Collector Mobile login is scoped to the golden default tenant without a picker or invite; another LGU needs
/// an explicit confirmed binding (which bypasses this rule entirely). Ambiguity never picks an arbitrary tenant.
/// </summary>
public class LoginTenantTests
{
    private static MunicipalityDto Lgu(string code, bool active, bool isDefault = false) =>
        new(code, code, "Surigao del Sur", "Office", active ? "Active" : "Inactive", active, isDefault);

    [Fact]
    public void OnlyCantilanLive_ResolvesCantilan()
    {
        Assert.Equal("CANTILAN", LoginTenant.ResolveUnbound([Lgu("CANTILAN", true, true), Lgu("MADRID", false), Lgu("CARMEN", false)]));
    }

    [Fact]
    public void SeveralLive_ResolvesTheExplicitDefault_NotTheFirstAlphabetically()
    {
        // Carmen sorts before Cantilan's peers and is live, yet the default decides.
        Assert.Equal("CANTILAN", LoginTenant.ResolveUnbound([Lgu("CARMEN", true), Lgu("CANTILAN", true, true), Lgu("MADRID", true)]));
    }

    [Fact]
    public void SeveralLive_WithNoDefault_FailsClosed()
    {
        Assert.Null(LoginTenant.ResolveUnbound([Lgu("CARMEN", true), Lgu("MADRID", true)]));
    }

    [Fact]
    public void SeveralLive_WithMoreThanOneDefault_IsDetectedAndFailsClosed()
    {
        Assert.Null(LoginTenant.ResolveUnbound([Lgu("CANTILAN", true, true), Lgu("MADRID", true, true)]));
    }

    [Fact]
    public void AnInactiveDefault_IsNeverChosen()
    {
        Assert.Null(LoginTenant.ResolveUnbound([Lgu("CANTILAN", false, true), Lgu("MADRID", true), Lgu("CARMEN", true)]));
    }
}

using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Tenancy;
using Moq;

namespace EEMOCantilanSDS.Testing.Phase2;

/// <summary>
/// PHASE 2 — Claim-bound tenant seam. The active LGU comes from the authenticated user's municipality
/// claim; with no claim (background jobs, tests, or — today — any user), it falls back to the default
/// tenant so behaviour is identical to the previous static resolver.
/// </summary>
public class ClaimTenantContextTests
{
    private static ClaimTenantContext WithMunicipality(string? code)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.MunicipalityCode).Returns(code);
        return new ClaimTenantContext(user.Object, new RequestTenantScope());
    }

    [Fact]
    public void UsesMunicipalityClaim_WhenPresent()
        => Assert.Equal("carrascal", WithMunicipality("carrascal").TenantCode);

    [Fact]
    public void TrimsClaimValue()
        => Assert.Equal("madrid", WithMunicipality("  madrid  ").TenantCode);

    [Fact]
    public void FallsBackToDefault_WhenClaimMissing()
        => Assert.Equal(TenantConstants.DefaultTenantCode, WithMunicipality(null).TenantCode);

    [Fact]
    public void FallsBackToDefault_WhenClaimBlank()
        => Assert.Equal(TenantConstants.DefaultTenantCode, WithMunicipality("   ").TenantCode);
}

using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileBindInfo;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetMobileBindInfoQueryHandlerTests
{
    private static GetMobileBindInfoQueryHandler Build(Municipality? m)
    {
        var repo = new Mock<IMunicipalityRepository>();
        repo.Setup(r => r.GetByBindTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(m);
        return new GetMobileBindInfoQueryHandler(repo.Object);
    }

    [Fact]
    public async Task ActiveMunicipality_ResolvesBranding()
    {
        var m = Municipality.Create("CARRASCAL", "Carrascal", "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: "carrascal", officeName: "Carrascal Economic Enterprise Office");
        var result = await Build(m).Handle(new GetMobileBindInfoQuery("tok"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("CARRASCAL", result.Value!.MunicipalityCode);
        Assert.Equal("carrascal", result.Value.TenantCode);
    }

    [Fact]
    public async Task UnknownToken_IsNotFound()
    {
        var result = await Build(null).Handle(new GetMobileBindInfoQuery("nope"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task InactiveMunicipality_IsNotFound()
    {
        var m = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Upcoming, tenantCode: "madrid");
        var result = await Build(m).Handle(new GetMobileBindInfoQuery("tok"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}

using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EEMOCantilanSDS.Testing.Api;

public class MobileVersionControllerTests
{
    private static MobileAppVersionDto Invoke(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var controller = new MobileVersionController(Mock.Of<ISender>(), config);
        var action = controller.GetVersion();
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<MobileAppVersionDto>(ok.Value);
    }

    [Fact]
    public void Defaults_ReportNoUpdate_AndFallbackApkUrl()
    {
        var dto = Invoke(new Dictionary<string, string?>());

        Assert.Equal(1, dto.LatestVersionCode);          // installed builds are >= 1 → no update prompted
        Assert.Equal(0, dto.MinSupportedVersionCode);    // nothing is ever mandatory by default
        Assert.Equal("https://app.stalltrack.site/download/stalltrack-collector-latest.apk", dto.ApkUrl);
        Assert.Null(dto.Notes);
    }

    [Fact]
    public void ConfiguredValues_AreReturned()
    {
        var dto = Invoke(new Dictionary<string, string?>
        {
            ["Mobile:LatestVersionCode"] = "5",
            ["Mobile:LatestVersion"] = "1.4.0",
            ["Mobile:MinSupportedVersionCode"] = "3",
            ["Mobile:DownloadUrl"] = "https://cdn.example/app.apk",
            ["Mobile:UpdateNotes"] = "Bug fixes and speed improvements.",
        });

        Assert.Equal(5, dto.LatestVersionCode);
        Assert.Equal("1.4.0", dto.LatestVersion);
        Assert.Equal(3, dto.MinSupportedVersionCode);
        Assert.Equal("https://cdn.example/app.apk", dto.ApkUrl);
        Assert.Equal("Bug fixes and speed improvements.", dto.Notes);
    }

    [Fact]
    public void ApkUrl_DerivesFromAppBaseUrl_WhenDownloadUrlUnset()
    {
        var dto = Invoke(new Dictionary<string, string?>
        {
            ["Mobile:AppBaseUrl"] = "https://app.example.gov/",
        });

        Assert.Equal("https://app.example.gov/download/stalltrack-collector-latest.apk", dto.ApkUrl);
    }
}

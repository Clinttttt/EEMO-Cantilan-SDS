using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;
public sealed class TerminalOverviewCardTests : TestContext
{
    private readonly Mock<IOfficeSourcesApiClient> _api=new();
    public TerminalOverviewCardTests() {
        Services.AddSingleton(_api.Object); Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>()); Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode=JSRuntimeMode.Loose;
    }
    [Fact] public void CardUsesTerminalNetActivityAndGenuineCountWithoutTripMetrics()
    {
        var today=PhilippineTime.Today;
        SourceNativeActivityDto Row(decimal amount,decimal net)=>new(Guid.NewGuid(),"SRC-test",today,DateTime.UtcNow,CollectorOperationCodes.Terminal,TerminalSection.ComfortRoom,null,null,null,null,amount,net,net==0?"Reversed":"Posted",null,null,null,null,null);
        _api.Setup(x=>x.ActivityAsync(new(today.Year,today.Month,1),new DateOnly(today.Year,today.Month,1).AddMonths(1).AddDays(-1),CollectorOperationCodes.Terminal)).ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([Row(70,70),Row(100,0)]));
        var cut=RenderComponent<TerminalOverviewCard>(p=>p.Add(x=>x.Year,today.Year).Add(x=>x.Month,today.Month));
        cut.WaitForAssertion(()=>Assert.Contains("₱70",cut.Markup));
        Assert.Equal(new[]{"₱70","1"},cut.FindAll("dd").Select(x=>x.TextContent));
        Assert.Equal("/operations/terminal",cut.Find("a").GetAttribute("href"));
        // IFT is the visible acronym; the persisted FacilityCode.TRM and the Terminal route are unchanged.
        Assert.Equal("IFT",cut.Find("small").TextContent); Assert.Equal("Income From Terminal",cut.Find("strong").TextContent);
        Assert.DoesNotContain("TRM",cut.Markup);
        Assert.DoesNotContain("Trips",cut.Markup); Assert.DoesNotContain("Drivers",cut.Markup); Assert.DoesNotContain("%",cut.Markup);
        Export("terminal-overview",cut.Markup);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void EmptyAndUnavailableAreDifferentStates(bool failed)
    {
        _api.Setup(x=>x.ActivityAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>(),CollectorOperationCodes.Terminal)).ReturnsAsync(failed?Result<IReadOnlyList<SourceNativeActivityDto>>.Failure("Unavailable"):Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]));
        var cut=RenderComponent<TerminalOverviewCard>(p=>p.Add(x=>x.Year,2026).Add(x=>x.Month,9));
        if(failed) { cut.WaitForAssertion(()=>Assert.Contains("unavailable",cut.Markup)); Assert.Empty(cut.FindAll("dd")); }
        else { cut.WaitForAssertion(()=>Assert.Contains("Collected",cut.Markup)); Assert.Equal("₱0",cut.Find("dd").TextContent); }
        Export(failed?"terminal-overview-error":"terminal-overview-zero",cut.Markup);
    }
    private static void Export(string name,string markup) {
        var directory=Environment.GetEnvironmentVariable("STALLTRACK_FINE_FIXTURE_DIR"); if(string.IsNullOrEmpty(directory)) return;
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../..")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,name+".html"),"<html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='"+new Uri(Path.Combine(root,"EEMOCantilanSDS.Client/wwwroot/app.css"))+"'><link rel='stylesheet' href='"+new Uri(Path.Combine(root,"EEMOCantilanSDS.Client/obj/Release/net10.0/scopedcss/bundle/EEMOCantilanSDS.Client.styles.css"))+"'></head><body><main style='max-width:380px'>"+markup+"</main></body></html>");
    }
}

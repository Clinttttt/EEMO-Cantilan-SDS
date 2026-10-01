using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.UnitTest.Mobile;

/// <summary>The launch screen never delays a ready app and never cycles messages; a failed restore always signs in.</summary>
public class LaunchStateTests
{
    [Fact]
    public void AFastStart_ShowsOnlyTheBrand()
    {
        Assert.Null(LaunchState.StatusFor(TimeSpan.Zero));
        Assert.Null(LaunchState.StatusFor(TimeSpan.FromMilliseconds(399)));
    }

    [Fact]
    public void ANoticeableWait_SaysStarting_ThenOnceRestoring()
    {
        Assert.Equal(LaunchState.Starting, LaunchState.StatusFor(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(LaunchState.Starting, LaunchState.StatusFor(TimeSpan.FromSeconds(2)));
        Assert.Equal(LaunchState.Restoring, LaunchState.StatusFor(TimeSpan.FromSeconds(3)));
        Assert.Equal(LaunchState.Restoring, LaunchState.StatusFor(TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(true, "/menu")]
    [InlineData(false, "/login")]
    public void TheRouteFollowsTheRestoreResult(bool restored, string route) =>
        Assert.Equal(route, LaunchState.RouteFor(restored));
}

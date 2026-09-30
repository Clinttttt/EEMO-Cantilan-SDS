namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Keeps the ECF page on StallTrack's Philippine business clock for period selection and UTC timestamp display.
/// </summary>
public class EcfPhilippineTimeTests
{
    [Fact]
    public void BillingPeriodAndRecordedTimeUsePhilippineTime()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var page = File.ReadAllText(Path.Combine(
            dir!.FullName,
            "EEMOCantilanSDS.Client",
            "Components",
            "Pages",
            "Menus",
            "ElectricityConsumptionFees.razor"));

        Assert.Contains("private DateTime _period = PhilippineTime.Today.ToDateTime(TimeOnly.MinValue);", page);
        Assert.Contains("_period = PhilippineTime.Today.ToDateTime(TimeOnly.MinValue);", page);
        Assert.Contains("PhilippineTime.ToPhilippineTime(activity.RecordedAtUtc).ToString(\"HH:mm\")", page);
        Assert.DoesNotContain("DateTime.Today", page);
        Assert.DoesNotContain("RecordedAtUtc.ToLocalTime()", page);
    }
}

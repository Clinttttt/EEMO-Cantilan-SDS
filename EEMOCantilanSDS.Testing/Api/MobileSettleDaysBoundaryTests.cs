using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace EEMOCantilanSDS.Testing.Api;

/// <summary>
/// A collector may settle several owed days at once, and gained nothing else in the process.
///
/// Asked for on 2026-08-24: a payor clearing four owed days at once is ordinary in the field, and the office's portal
/// could already record it in one act while the collector app could only do one day at a time.
///
/// The obvious route was to let collectors call the portal's own endpoint, which would have widened a role on a money
/// endpoint that also settles whole months and edits receipts. The collector app has its own controller, already
/// collectors-only, so it got its own endpoint onto the SAME command instead — and the command's own guard
/// (NpmSettlementAccess, tested separately) already restricts a collector to a facility they are assigned to.
///
/// Asserted on the source because it is a boundary rather than a behaviour: what must stay true is that the collector
/// route exists, that it sends the same command, and that the administrators' endpoint was left alone.
/// </summary>
public class MobileSettleDaysBoundaryTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void TheCollectorAppSettlesSeveralDaysThroughItsOwnCollectorOnlyController()
    {
        var mobile = Source("EEMOCantilanSDS.Api", "Controllers", "MobileController.cs");

        // Collectors only, as this controller has always been.
        Assert.Contains("[Authorize(Roles = \"Collector\")]", mobile);

        // The route the app calls, sending the same command the portal sends.
        Assert.Contains("npm/collections/settle-days", mobile);
        Assert.Contains("new SettleNpmDaysCommand(", mobile);
    }

    [Fact]
    public void TheAdministratorsSettleDaysEndpointWasNotWidened()
    {
        // The point of the shape above: nobody gained access to the portal's endpoint, which sits beside settle-month and
        // the receipt editors. If a later change adds Collector here, it should be a decision taken deliberately - this
        // records that it was not needed for the collector app to settle several days.
        var controller = Source("EEMOCantilanSDS.Api", "Controllers", "DailyCollectionsController.cs");

        var settleDays = Regex.Match(
            controller,
            @"\[HttpPost\(""settle-days""\)\]\s*\r?\n\s*\[Authorize\(Roles = ""(?<roles>[^""]+)""\)\]");

        Assert.True(settleDays.Success, "settle-days should still declare its roles right above the action.");
        Assert.Equal("SuperAdmin,Admin", settleDays.Groups["roles"].Value);
    }

    [Fact]
    public void TheAppSendsTheDaysTheCollectorChose()
    {
        // The days themselves, not a count: which days the money answers for is what the office reconciles against, and
        // the server decides which of them can actually be settled.
        var client = Source("EEMOCantilanSDS.HttpClients", "ApiClients", "MobileApiClient.cs");
        var sheet = Source("EEMOCantilanSDS.Mobile", "Components", "Pages", "Menus", "Market.razor");

        Assert.Contains("api/Mobile/npm/collections/settle-days", client);
        Assert.Contains("SettleNpmDaysAsync(new SettleMobileNpmDaysRequest(", sheet);
        Assert.Contains("ChosenDays.OrderBy(d => d).ToList()", sheet);

        // Several days is a statement about money collected. "Not collected" and "Absent" answer for one day, so they
        // keep the per-day path — including the offline queue, which is per day.
        Assert.Contains("SelectedIsPaid && !SelectedIsAbsent && ChosenDays.Count > 1", sheet);
    }
}

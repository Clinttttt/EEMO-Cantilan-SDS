using Bunit;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Operation report pages open on the Monthly view of the operation reports layout (OperationReportsPage); the official
/// report a test reads is the Status Report tab.
/// </summary>
internal static class OperationReportsLayout
{
    public static void OpenStatusReport(this IRenderedFragment cut) =>
        cut.FindAll("button.orp-tab").Single(b => b.TextContent.Contains("Status Report")).Click();

    /// <summary>Steps the layout's period navigator back one month (the report reloads for it).</summary>
    public static Task PreviousMonthAsync(this IRenderedFragment cut) =>
        cut.Find("button[aria-label='Previous period']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
}

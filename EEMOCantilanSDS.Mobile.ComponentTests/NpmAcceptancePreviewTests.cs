using Bunit;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>Real shared Mobile summary; mock DTO only, never a Windows app or authenticated API session.</summary>
public sealed class NpmAcceptancePreviewTests : TestContext
{
    [Theory]
    [InlineData(25)] [InlineData(20)] [InlineData(0)]
    public void Synthetic_month_summary_preserves_the_exact_server_amount(decimal amount)
    {
        var quote = new NpmWholePaymentQuoteDto(Guid.NewGuid(), 2026, 10, new(2026, 10, 17),
            0, amount, 0, "synthetic-preview-token", RevenueInstrumentType.OfficialReceipt,
            900, 900 - amount, 0, CollectionSourceKind.NpmRentMonth, 4, Guid.NewGuid());
        var cut = RenderComponent<NpmWholeSummary>(p => p.Add(x => x.Quote, quote));
        Assert.Contains(amount.ToString("N2"), cut.Markup);
        Assert.DoesNotContain("30 days", cut.Markup);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EEMOCantilanSDS.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var css = File.ReadAllText(Path.Combine(root.FullName, "EEMOCantilanSDS.Mobile/Components/Shared/NpmWholeSummary.razor.css"));
        var directory = Path.Combine(Path.GetTempPath(), "stalltrack-npm-acceptance"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"mobile-summary-{amount:0}.html"),
            "<!doctype html><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
            "<title>Synthetic Mobile summary - no service</title><style>" + css +
            "body{font-family:Arial,sans-serif;margin:16px}:root{--navy:#173b61;--border:#cfd6de;--bg-card:white;--text:#172b42}</style>" + cut.Markup);
    }
}

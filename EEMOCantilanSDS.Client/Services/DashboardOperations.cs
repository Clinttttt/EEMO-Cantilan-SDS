using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// The Dashboard's presentation map for operations that are NOT facility cards: the official revenue-source row key to the
/// existing operation workspace it opens. Money, counts, labels and groups always come from the official source projection
/// (<see cref="RevenueSourcePerformanceRowDto"/>); this adds only a route, an action label and a glyph.
/// </summary>
/// <remarks>
/// Facility-backed sources (NPM/TCC/NCC/BBQ/ICE rent, Tabo, Slaughterhouse) and Income From Terminal already have their own
/// Dashboard cards and are deliberately absent, so no operation is shown twice. Arrears is a receivable, not collected
/// revenue, and a classification with no workspace (OTHER_*) stays visible only in the Revenue sources view.
/// The routes mirror <c>Operations.razor</c>; a test keeps the two in step.
/// </remarks>
public static class DashboardOperations
{
    public sealed record Entry(string Key, string Route, string Action, string IconMarkup);

    private const string Market = "<path d=\"M3 9l1.5-5h15L21 9\"/><path d=\"M4 9v11h16V9\"/><path d=\"M9 20v-6h6v6\"/>";
    private const string Bolt = "<polygon points=\"13 2 3 14 12 14 11 22 21 10 12 10 13 2\"/>";
    private const string Drop = "<path d=\"M12 2.7s6 6.3 6 10.6a6 6 0 0 1-12 0C6 9 12 2.7 12 2.7z\"/>";
    private const string Fish = "<path d=\"M2 12s4-6 10-6 8 6 8 6-2 6-8 6S2 12 2 12z\"/><circle cx=\"16\" cy=\"11\" r=\"1\"/>";
    private const string Anchor = "<circle cx=\"12\" cy=\"5\" r=\"2\"/><path d=\"M12 7v14\"/><path d=\"M5 12H3a9 9 0 0 0 18 0h-2\"/>";
    private const string Car = "<path d=\"M5 17h14v-5l-2-5H7l-2 5z\"/><circle cx=\"8\" cy=\"17\" r=\"1.5\"/><circle cx=\"16\" cy=\"17\" r=\"1.5\"/>";
    private const string Scale = "<path d=\"M12 3v18\"/><path d=\"M5 7h14\"/><path d=\"M5 7l-3 7a3 3 0 0 0 6 0z\"/><path d=\"M19 7l-3 7a3 3 0 0 0 6 0z\"/>";
    private const string Transfer = "<path d=\"M17 1l4 4-4 4\"/><path d=\"M3 11V9a4 4 0 0 1 4-4h14\"/><path d=\"M7 23l-4-4 4-4\"/><path d=\"M21 13v2a4 4 0 0 1-4 4H3\"/>";
    private const string Spaces = "<rect x=\"3\" y=\"3\" width=\"7\" height=\"7\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"7\"/><rect x=\"3\" y=\"14\" width=\"7\" height=\"7\"/><rect x=\"14\" y=\"14\" width=\"7\" height=\"7\"/>";
    private const string Flag = "<path d=\"M4 22V4\"/><path d=\"M4 4h13l-2 4 2 4H4\"/>";
    private const string Fine = "<path d=\"M14 2H6a2 2 0 0 0-2 2v16l3-2 3 2 3-2 3 2V8z\"/><path d=\"M14 2v6h6\"/><path d=\"M9 13h6\"/>";

    /// <summary>In the order the Operations register lists them.</summary>
    public static readonly IReadOnlyList<Entry> Direct =
    [
        new("MARKET_FEES", "/operations/market-fees", "Open Market Fees", Market),
        new("ECF", "/operations/ecf", "Open ECF", Bolt),
        new("WCF", "/operations/water-consumption-fees", "Open WCF", Drop),
        new("FISH_MEAT_VENDOR_FEE", "/operations/fish-meat-vendor-fees", "Open Vendor Fees", Fish),
        new("LANDING_BERTHING", "/operations/landing-berthing", "Open Landing / Berthing", Anchor),
        new("TRANSPORTATION_PARKING", "/operations/transportation", "Open Transportation", Car),
        new("WEIGHT_AND_MEASURE", "/operations/weight-and-measure", "Open Weight & Measure", Scale),
        new("TRANSFER_LARGE_CATTLE", "/operations/transfer-large-cattle", "Open Transfers", Transfer),
        new("VEGETABLE_FRUIT_SPACE_RENTAL", "/operations/vegetable-fruit", "Open Vegetable | Fruits", Spaces),
        new("KANMANGGAY_SPACE_RENTAL", "/operations/kanmanggay", "Open Kanmanggay", Spaces),
        new("FIESTA_ARAW_LOT_RENTAL", "/operations/fiesta-araw", "Open Fiesta | Araw", Flag),
        new("PENALTIES_AND_FINES", "/operations/fines", "Open Fines & Penalties", Fine),
    ];

    public static Entry? For(string rowKey) => Direct.FirstOrDefault(e => e.Key == rowKey);

    /// <summary>The cards to show, in register order, for the rows the official projection actually returned.</summary>
    public static IReadOnlyList<(Entry Entry, RevenueSourcePerformanceRowDto Row)> CardsFor(RevenueSourcePerformanceDto? source) =>
        source is null
            ? []
            : Direct.Select(e => (Entry: e, Row: source.Rows.FirstOrDefault(r => r.Key == e.Key)))
                .Where(x => x.Row is not null).Select(x => (x.Entry, x.Row!)).ToList();

    /// <summary>How the source is paid, in the office's terms; never a collection rate, which a direct service has no denominator for.</summary>
    public static string ModelLabel(string model) => model switch
    {
        RevenueSourceModel.Transactional => "Paid on service",
        RevenueSourceModel.RecurringObligation => "Monthly obligation",
        RevenueSourceModel.QuantityService => "Per quantity",
        RevenueSourceModel.EventRental => "Per event",
        _ => ""
    };
}

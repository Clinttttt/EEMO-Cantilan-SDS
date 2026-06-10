namespace EEMOCantilanSDS.Client.Components.Pages.Payor;

/// <summary>
/// Hardcoded demo data for the Payor Portal preview. Shapes mirror the planned
/// GetPayorBalances / GetPayorPaymentHistory query DTOs so they can be swapped in later.
/// </summary>
public static class PayorDemoData
{
    public record PayorInfo(string Name, string Contact, string Email);
    public record StallAccount(string Facility, string StallNo, string Occupant, decimal MonthlyRate);
    public record OutstandingItem(string Period, string Facility, decimal Amount, DateOnly DueDate, string Status);
    public record HistoryItem(DateOnly Date, string Period, string Facility, decimal Amount, string? OrNumber, string Status);

    public static readonly PayorInfo Payor =
        new("Maria Dela Cruz", "0917 ••• 2234", "maria.delacruz@gmail.com");

    public static readonly List<StallAccount> Accounts = new()
    {
        new("Tampak Commercial Center", "12", "Maria Dela Cruz", 2400m),
    };

    public static readonly List<OutstandingItem> Outstanding = new()
    {
        new("June 2026", "Tampak Commercial Center", 2400m, new DateOnly(2026, 6, 30), "Unpaid"),
        new("May 2026",  "Tampak Commercial Center", 1200m, new DateOnly(2026, 5, 31), "Partial"),
    };

    public static readonly List<HistoryItem> History = new()
    {
        new(new DateOnly(2026, 5, 12), "May 2026 (partial)", "Tampak Commercial Center", 1200m, null, "Awaiting OR"),
        new(new DateOnly(2026, 4, 28), "April 2026", "Tampak Commercial Center", 2400m, "124533", "Paid"),
        new(new DateOnly(2026, 3, 30), "March 2026", "Tampak Commercial Center", 2400m, "120114", "Paid"),
        new(new DateOnly(2026, 2, 27), "February 2026", "Tampak Commercial Center", 2400m, "118402", "Paid"),
    };

    public static decimal TotalOutstanding => Outstanding.Sum(o => o.Amount);

    public static DateOnly EarliestDue =>
        Outstanding.Any() ? Outstanding.Min(o => o.DueDate) : DateOnly.FromDateTime(DateTime.Now);

    public static string FormatCurrency(decimal value) => $"₱{value:N2}";

    public static string StatusClass(string status) => status switch
    {
        "Paid" => "badge-paid",
        "Partial" => "badge-partial",
        "Awaiting OR" => "badge-awaiting",
        _ => "badge-unpaid"
    };
}

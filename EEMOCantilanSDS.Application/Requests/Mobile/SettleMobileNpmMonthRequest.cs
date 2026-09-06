using System;

namespace EEMOCantilanSDS.Application.Requests.Mobile
{
    /// <summary>
    /// A closed month of one market stall, settled in the field at the office's own figure for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A month that has closed owing is NOT a set of days, which is why the collector app could not take one and the arrears
    /// screen said so. Where the office lets a stall for a monthly rent, the month owes that rent whatever its calendar gave
    /// it: a 31-day month at ₱30 owes ₱900, not ₱930, the last installments being folded into a month-end difference. Offering
    /// its days as chips would have collected ₱930 and called it settled.
    /// </para>
    /// <para>
    /// So the month is sent as a month, and the server prices it by the same settlement the office's own portal uses. The
    /// office confirmed 2026-09-06 that collectors may take past months, which is what this carries - not a new rule for what
    /// a month costs.
    /// </para>
    /// </remarks>
    /// <param name="ORNumber">The receipt for the month, when the office issued one.</param>
    public sealed record SettleMobileNpmMonthRequest(
        Guid StallId,
        int Year,
        int Month,
        string? ORNumber);
}

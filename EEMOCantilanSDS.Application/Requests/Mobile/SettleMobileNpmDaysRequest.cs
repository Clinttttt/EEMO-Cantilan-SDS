using System;
using System.Collections.Generic;

namespace EEMOCantilanSDS.Application.Requests.Mobile
{
    /// <summary>
    /// Several owed days of one market stall, settled together against one physical receipt.
    ///
    /// <para>
    /// A payor clearing four owed days at once is ordinary in the field. The collector app could only record one day at a
    /// time, so four days meant four confirmations and four chances to stop half way; the office's own portal has always
    /// been able to record them together. This carries the same three things the portal sends.
    /// </para>
    /// </summary>
    /// <param name="Dates">
    /// The days the money answers for. Sent as the days the collector actually chose rather than a count, because which
    /// days were settled is what the office reconciles against. The server skips any that are future, market-closed,
    /// already paid or excused, or that no term answers for, and refuses the request when that leaves nothing.
    /// </param>
    /// <param name="ORNumber">
    /// The one receipt covering them, when the office issued one. Uniqueness is per stall, so the same OR may repeat
    /// across this stall's days — that is what one physical receipt for four days means.
    /// </param>
    public sealed record SettleMobileNpmDaysRequest(
        Guid StallId,
        IReadOnlyList<DateOnly>? Dates,
        string? ORNumber);
}

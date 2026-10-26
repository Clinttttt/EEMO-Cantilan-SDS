using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>
/// The Arrears list the collector has just been shown, kept briefly so "Collect arrears" does not rebuild the whole list from the office
/// just to find the one account that was tapped (that read walks every delinquent and lapsed account and can take many seconds).
/// </summary>
/// <remarks>
/// Convenience only, never authority: the checkout that follows asks the office again (assignment, source, balance) before any amount is quoted
/// or posted. It is handed back only to the collector it was read for and only while fresh, so a different sign-in or an old list is ignored and
/// the page reads the list again.
/// </remarks>
public sealed class ArrearsWorkspaceHolder(Func<DateTime>? utcNow = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly Func<DateTime> _now = utcNow ?? (() => DateTime.UtcNow);
    private Guid _owner;
    private DateTime _at;
    private IReadOnlyList<ArrearsAccountDto> _accounts = [];

    public void Set(Guid collectorId, IReadOnlyList<ArrearsAccountDto> accounts) { _owner = collectorId; _at = _now(); _accounts = accounts; }

    public void Clear() { _owner = Guid.Empty; _accounts = []; }

    /// <summary>The account for this collector, or null when nothing fresh is held for them (or the account is not in it).</summary>
    public ArrearsAccountDto? Find(Guid collectorId, ArrearsAccountKind kind, Guid identityId) =>
        collectorId != Guid.Empty && collectorId == _owner && _now() - _at <= Lifetime
            ? _accounts.FirstOrDefault(a => a.Kind == kind && a.Identity.Id == identityId)
            : null;
}

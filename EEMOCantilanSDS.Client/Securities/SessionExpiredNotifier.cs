namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// Circuit-scoped signal raised when the API rejects a token refresh — i.e. the session is truly over
/// (refresh token expired, revoked on logout, or superseded by a login elsewhere; sessions are
/// single-per-account). A layout-level guard subscribes and redirects to the appropriate login.
/// Registered SCOPED so the refresh handler (resolved via <see cref="CircuitServicesAccessor"/>) and the
/// UI guard share one instance per circuit. <see cref="NotifyExpired"/> fires once per circuit so the
/// burst of 401s that follows an expiry triggers a single redirect, not many.
/// </summary>
public sealed class SessionExpiredNotifier
{
    private int _signalled;

    public event Action? SessionExpired;

    public void NotifyExpired()
    {
        if (Interlocked.Exchange(ref _signalled, 1) == 0)
            SessionExpired?.Invoke();
    }
}

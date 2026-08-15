namespace EEMOCantilanSDS.Application.Common;

/// <summary>
/// What KIND of outcome a handler reached, stated without reference to HTTP.
///
/// <para>
/// Application used to name HTTP status codes directly — a bare 409 or 502 as the second argument to a failure — which put
/// a fact about a web API inside the layer that decides what a market, a stall and a receipt owe. A handler has no use for the
/// number 409; what it knows is that the thing already exists. The API translates these to status codes at its own boundary
/// (<c>ApiBaseController.HandleResponse</c>), which is the only place that should know how this office is reached.
/// </para>
///
/// <para>
/// The set is deliberately small and matches what the code actually produces — no speculative categories. Each maps to exactly
/// one status, and <c>HandleResponseContractTests</c> holds that mapping to what the portal, the mobile app and the sync path
/// already read.
/// </para>
/// </summary>
public enum ResultStatus
{
    /// <summary>Succeeded, with a value. (200)</summary>
    Ok = 0,

    /// <summary>Succeeded with nothing to return. (204)</summary>
    NoContent,

    /// <summary>The request itself was wrong — a bad figure, a missing field, a rule refused. (400)</summary>
    Invalid,

    /// <summary>Not signed in, or the credentials did not check out. (401)</summary>
    Unauthorized,

    /// <summary>Signed in, but not permitted to do this. (403)</summary>
    Forbidden,

    /// <summary>No such record. (404)</summary>
    NotFound,

    /// <summary>It already exists, or another record holds the value — a duplicate OR number, a taken username. (409)</summary>
    Conflict,

    /// <summary>
    /// The account is temporarily locked after repeated failed sign-ins. (423)
    /// <para>Distinct from <see cref="Unauthorized"/> on purpose: it is only ever returned once the password itself checked
    /// out, so its message may be shown. A wrong password stays a bare 401.</para>
    /// </summary>
    Locked,

    /// <summary>Something on our side failed. (500)</summary>
    Failed,

    /// <summary>Something we depend on failed — the backup runner, the payment gateway. (502)</summary>
    UpstreamFailed,
}

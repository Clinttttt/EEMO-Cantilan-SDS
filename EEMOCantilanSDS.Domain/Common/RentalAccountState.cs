namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// What a rental account is, on a business date, for every screen and rule that has to agree: the Operations registers and counts, the profile, Closed
/// Accounts and the lifecycle actions. An account's end date is its LAST day, inclusive (the domain's <c>IsActiveOn</c> and the collection lists agree), so an
/// end date that has not passed is still an active account, billed to that date. Nothing here reads or changes a financial record.
/// </summary>
public enum RentalAccountState
{
    Active = 1,
    /// <summary>An end date is recorded and has not passed: still active and collectible until then.</summary>
    Ending = 2,
    /// <summary>Open, but the recorded signed term has run out.</summary>
    TermExpired = 3,
    /// <summary>The end date has passed.</summary>
    Closed = 4,
    /// <summary>Closed, and someone now holds the same space or stall.</summary>
    Superseded = 5,
    /// <summary>Closed and retired from the roll; its records are kept.</summary>
    Retired = 6
}

public static class RentalAccountStates
{
    /// <summary>Whether the end date has passed (the last day was before <paramref name="asOf"/>).</summary>
    public static bool HasEnded(DateOnly? endsOn, DateOnly asOf) => endsOn is { } end && end < asOf;

    public static RentalAccountState Classify(DateOnly? endsOn, bool retired, bool hasSuccessor, DateOnly? termLastDay, DateOnly asOf)
    {
        if (retired) return RentalAccountState.Retired;
        if (HasEnded(endsOn, asOf)) return hasSuccessor ? RentalAccountState.Superseded : RentalAccountState.Closed;
        if (endsOn is not null) return RentalAccountState.Ending;
        return termLastDay is { } last && last < asOf ? RentalAccountState.TermExpired : RentalAccountState.Active;
    }

    /// <summary>Open accounts are the ones still collectible and manageable as active: Active, Ending and TermExpired.</summary>
    public static bool IsOpen(RentalAccountState state) => state is RentalAccountState.Active or RentalAccountState.Ending or RentalAccountState.TermExpired;
}

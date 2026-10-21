using EEMOCantilanSDS.Application.Common.Revenue;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

/// <summary>The one rule set behind single confirm, bulk confirm and the review list.</summary>
public sealed class DirectCollectionVerificationTests
{
    private const string Awaiting = DirectCollectionVerification.Awaiting;
    private const string Confirmed = DirectCollectionVerification.Confirmed;
    private const string Flagged = DirectCollectionVerification.NeedsCorrection;
    private const string Resolved = DirectCollectionVerification.Resolved;

    [Theory]
    [InlineData("Posted", Awaiting, false, false, null, null)]                    // Pending -> Confirmed
    [InlineData("Posted", Resolved, false, false, null, null)]                    // Pending -> Needs correction -> Resolved -> Confirmed
    [InlineData("Document corrected", Awaiting, false, false, null, null)]       // a replaced document is still the same cash
    [InlineData("Reversed", Awaiting, false, false, null, "Reversed")]
    [InlineData("Reversed", Flagged, false, false, null, "Reversed")]
    [InlineData("Posted", Confirmed, false, false, null, "AlreadyConfirmed")]    // a stale or duplicate confirm
    [InlineData("Posted", Flagged, false, false, null, "CorrectionUnresolved")]  // resolving is required first
    [InlineData("Posted", Awaiting, false, true, null, "IdentityUnresolved")]
    [InlineData("Posted", Resolved, false, true, null, "IdentityUnresolved")]
    [InlineData("Posted", Awaiting, true, false, null, "PossibleDuplicate")]
    [InlineData("Posted", Awaiting, true, false, "too short", "PossibleDuplicate")]
    [InlineData("Posted", Awaiting, true, false, "Second payment, separate slip", null)]   // a written reason lets the Head confirm it on its own
    [InlineData("Posted", "Unknown", false, false, null, "NotReviewable")]
    public void Confirmation_follows_one_rule_set(string disposition, string state, bool duplicate, bool needsRegistration, string? note, string? expected) =>
        Assert.Equal(expected, DirectCollectionVerification.ConfirmBlock(disposition, state, duplicate, needsRegistration, note));

    [Fact]
    public void A_reversal_wins_over_every_other_reason()
    {
        Assert.Equal("Reversed", DirectCollectionVerification.ConfirmBlock("Reversed", Confirmed, true, true, "Second payment, separate slip"));
    }

    [Fact]
    public void An_unnamed_walk_up_payor_is_not_by_itself_a_reason_to_refuse()
    {
        // Identity is only unresolved where a source requires a registration that was never linked; a source that permits a walk-up payor stays confirmable.
        Assert.Null(DirectCollectionVerification.ConfirmBlock("Posted", Awaiting, false, needsRegistration: false, null));
    }

    [Theory]
    [InlineData("Posted", Awaiting, null)]
    [InlineData("Posted", Confirmed, null)]          // a confirmed collection can still be flagged for correction
    [InlineData("Posted", Resolved, null)]
    [InlineData("Posted", Flagged, "AlreadyFlagged")]
    [InlineData("Reversed", Awaiting, "Reversed")]
    public void Flagging_is_refused_on_a_reversal_and_on_an_open_flag(string disposition, string state, string? expected) =>
        Assert.Equal(expected, DirectCollectionVerification.FlagBlock(disposition, state));

    [Theory]
    [InlineData(Flagged, null)]
    [InlineData(Awaiting, "NotFlagged")]
    [InlineData(Confirmed, "NotFlagged")]
    [InlineData(Resolved, "NotFlagged")]
    public void Only_an_open_flag_can_be_resolved_and_resolving_is_not_verifying(string state, string? expected) =>
        Assert.Equal(expected, DirectCollectionVerification.ResolveBlock(state));

    [Fact]
    public void Every_refusal_has_a_plain_message()
    {
        foreach (var code in new[] { "Reversed", "AlreadyConfirmed", "CorrectionUnresolved", "IdentityUnresolved", "PossibleDuplicate", "NotReviewable" })
            Assert.False(string.IsNullOrWhiteSpace(DirectCollectionVerification.BlockMessage(code)));
        Assert.Contains("Resolve it first", DirectCollectionVerification.BlockMessage("CorrectionUnresolved"));
    }
}

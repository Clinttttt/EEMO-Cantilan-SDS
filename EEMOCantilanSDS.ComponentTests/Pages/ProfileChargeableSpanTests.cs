using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Profile = EEMOCantilanSDS.Client.Components.Pages.Shared.Actions.Profile;

/// <summary>
/// The span of an earlier occupancy that the stall profile's activity grid may paint as owing.
///
/// <para>
/// The grid asks one question of each day — was this day CHARGEABLE — and a lapsed term charges nothing: the register bills
/// to min(end, ExpiryDate). The span was taken as the occupancy's recorded end instead, which is the day the lessee stopped
/// holding the stall and can be years later. NPM stall 6 showed 364 "Uncollected" days beside an earlier-term balance of
/// ₱10,800, twelve months — the grid and the figure printed next to it disagreed about the same term.
/// </para>
///
/// <para>
/// Tested directly rather than by rendering, because the grid only exists on the market profile, whose fixture also fetches
/// rates and a month of collections; the arithmetic is the part that was wrong.
/// </para>
/// </summary>
public class ProfileChargeableSpanTests
{
    private static readonly Guid StallId = Guid.NewGuid();

    private static ClosedStallAccountDto Account(DateOnly effectivity, int years, DateOnly expiry, DateOnly? endedOn) =>
        new(StallId, InactiveAccountState.Lapsed, FacilityCode.NPM, "New Public Market", "6",
            "Teofila Reyes", "Teofila Reyes",
            EffectivityDate: effectivity, DurationYears: years, MonthlyRate: 900m,
            ClosedOn: null, ExpiryDate: expiry,
            LifetimeCollected: 0m, Uncollected: 10_800m,
            ClosedBy: null, Section: "", OccupancyEndedOn: endedOn);

    [Fact]
    public void HoldingOnPastALapsedTerm_IsChargeableOnlyToTheExpiry()
    {
        // Stall 6 as the office actually holds it: a one-year term from 1 Jan 2023, recorded as ended on 11 Sep 2026 because
        // that is when it was renewed. Chargeable stops at the expiry — 31 Dec 2023, twelve months, the office's own figure.
        var span = Profile.ChargeableSpan(Account(
            effectivity: new DateOnly(2023, 1, 1),
            years: 1,
            expiry: new DateOnly(2023, 12, 31),
            endedOn: new DateOnly(2026, 9, 11)));

        Assert.Equal(new DateOnly(2023, 1, 1), span.Start);
        Assert.Equal(new DateOnly(2023, 12, 31), span.End);
    }

    [Fact]
    public void AnEarlyHandover_StillEndsWhereItActuallyEnded()
    {
        // The clamp must only ever shorten. A lessee who handed the stall over mid-term owes to the handover, not to a term
        // end they never reached.
        var span = Profile.ChargeableSpan(Account(
            effectivity: new DateOnly(2023, 1, 1),
            years: 3,
            expiry: new DateOnly(2025, 12, 31),
            endedOn: new DateOnly(2024, 6, 30)));

        Assert.Equal(new DateOnly(2024, 6, 30), span.End);
    }

    [Fact]
    public void NoRecordedEnd_RunsToTheExpiry()
    {
        var span = Profile.ChargeableSpan(Account(
            effectivity: new DateOnly(2023, 1, 1),
            years: 3,
            expiry: new DateOnly(2025, 12, 31),
            endedOn: null));

        Assert.Equal(new DateOnly(2025, 12, 31), span.End);
    }
}

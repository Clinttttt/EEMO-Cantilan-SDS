using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Every real-world collection has exactly one authoritative reporting path (IA-050). These tests fail if a new source
/// kind is added without deciding its authority, or if any kind/authority pair could be counted twice or not at all.
/// </summary>
public class CollectionSourceAuthorityMapTests
{
    public static IEnumerable<object[]> Kinds() =>
        Enum.GetValues<CollectionSourceKind>().Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKindHasADecidedAuthority_AndIsCountedExactlyOnceInEveryState(CollectionSourceKind kind)
    {
        _ = CollectionSourceAuthorityMap.For(kind);   // throws for an undecided kind

        foreach (var state in Enum.GetValues<SettlementAuthority>())
        {
            var legacy = CollectionSourceAuthorityMap.LegacyMoneyCounts(kind, state);
            var canonical = CollectionSourceAuthorityMap.CanonicalMoneyCounts(kind, state);

            // A legacy-only source's canonical rows are shadow evidence, so it is counted once (legacy); every other
            // combination is counted by exactly one representation. Never both, never neither.
            Assert.True(legacy ^ canonical, $"{kind} in {state} would be counted {(legacy ? "twice" : "zero times")}.");
        }
    }

    [Theory]
    [InlineData(CollectionSourceKind.PaymentRecord, SettlementAuthority.Legacy, true)]
    [InlineData(CollectionSourceKind.PaymentRecord, SettlementAuthority.PendingCutover, true)]
    [InlineData(CollectionSourceKind.PaymentRecord, SettlementAuthority.Canonical, false)]
    [InlineData(CollectionSourceKind.UtilityBill, SettlementAuthority.Canonical, false)]
    [InlineData(CollectionSourceKind.GovernedService, SettlementAuthority.Legacy, false)]
    [InlineData(CollectionSourceKind.ObligationPeriod, SettlementAuthority.Legacy, false)]
    [InlineData(CollectionSourceKind.TrmTrip, SettlementAuthority.Canonical, true)]
    public void LegacyMoneyCounts_OnlyBeforeACutover_AndNeverForACanonicalSource(
        CollectionSourceKind kind, SettlementAuthority state, bool expected) =>
        Assert.Equal(expected, CollectionSourceAuthorityMap.LegacyMoneyCounts(kind, state));

    [Fact]
    public void ANewSourceKindIsRefusedUntilItsAuthorityIsDecided() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CollectionSourceAuthorityMap.For((CollectionSourceKind)999));
}

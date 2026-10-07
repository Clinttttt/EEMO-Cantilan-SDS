using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>How a source's money is reported: which representation is the one authoritative record of it (IA-050).</summary>
public enum SourceReportingAuthority
{
    /// <summary>Only the legacy specialized source records this money today. Canonical rows for it are shadow evidence.</summary>
    LegacyOnly = 0,

    /// <summary>Legacy is authoritative for a source row until that row's settlement authority becomes Canonical; from then the canonical Collection is.</summary>
    CanonicalAfterRowCutover = 1,

    /// <summary>The source has no legacy representation: the canonical Collection is authoritative from birth.</summary>
    CanonicalAlways = 2
}

/// <summary>
/// The per-source reporting-authority map. Every real-world collection has exactly one authoritative reporting path, and
/// every report (Collector Records, Collector Report of Collections, Collection Activity, Monthly Income, RCD) decides
/// which representation to count through this map, never by adding a legacy total to a canonical total.
///
/// Legacy rows keep compatibility projections after a cutover (for example a PaymentRecord's status and partial amount),
/// so a converted row's legacy fields are comparison evidence and must not be counted again beside its canonical line.
/// A new <see cref="CollectionSourceKind"/> cannot be added without deciding its authority here: the exhaustive switch
/// and its test refuse an unmapped kind.
/// </summary>
public static class CollectionSourceAuthorityMap
{
    public static SourceReportingAuthority For(CollectionSourceKind kind) => kind switch
    {
        // An NPM day row is under legacy authority until a canonical Collection pays it (prospective cutover, IA-051).
        CollectionSourceKind.PaymentRecord or CollectionSourceKind.UtilityBill or CollectionSourceKind.DailyCollection
            => SourceReportingAuthority.CanonicalAfterRowCutover,

        CollectionSourceKind.GovernedService or CollectionSourceKind.PenaltyDefinition
            or CollectionSourceKind.ObligationPeriod or CollectionSourceKind.NpmWeighing or CollectionSourceKind.FishMeatVendorFee
            or CollectionSourceKind.FishMeatVendorRegistration or CollectionSourceKind.TerminalSection
            => SourceReportingAuthority.CanonicalAlways,

        CollectionSourceKind.SlaughterTransaction
            or CollectionSourceKind.TpmAttendance or CollectionSourceKind.TrmTrip
            or CollectionSourceKind.OnlinePaymentTransaction
            => SourceReportingAuthority.LegacyOnly,

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This source kind has no reporting authority decision.")
    };

    /// <summary>True when the legacy representation of this source row is the authoritative money.</summary>
    public static bool LegacyMoneyCounts(CollectionSourceKind kind, SettlementAuthority rowAuthority) => For(kind) switch
    {
        SourceReportingAuthority.LegacyOnly => true,
        SourceReportingAuthority.CanonicalAfterRowCutover => rowAuthority != SettlementAuthority.Canonical,
        _ => false
    };

    /// <summary>True when the canonical Collection line against this source row is the authoritative money.</summary>
    public static bool CanonicalMoneyCounts(CollectionSourceKind kind, SettlementAuthority rowAuthority) => For(kind) switch
    {
        SourceReportingAuthority.LegacyOnly => false,
        SourceReportingAuthority.CanonicalAfterRowCutover => rowAuthority == SettlementAuthority.Canonical,
        _ => true
    };
}

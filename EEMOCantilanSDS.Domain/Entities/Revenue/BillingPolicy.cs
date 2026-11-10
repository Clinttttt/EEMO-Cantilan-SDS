using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum LatePenaltyBasis { None = 0, PercentOfUnpaid = 1, FixedAmount = 2 }

/// <summary>
/// The rules one office bills monthly rent by: StallTrack Rules (IA-071, IA-072) its Head can change, not MEEDO rulings. A due date drives display,
/// reminders and penalties only: it never redefines Delinquent or Arrears (IA-019, IA-020, IA-069). A penalty rate is never assumed, so none applies
/// until the Head sets one.
/// </summary>
/// <param name="CountsMonthsFrom">The first of the month these rules begin at (IA-075). A term can begin years before an office kept its records here, so an earlier month with no record says nothing about whether it was paid.</param>
public sealed record BillingPolicy(
    int? DueDay,
    int GraceDays,
    bool OldestMonthFirst,
    int MaxAdvanceMonths,
    bool AnnualPaymentAllowed,
    LatePenaltyBasis PenaltyBasis,
    decimal? PenaltyValue,
    bool PenaltyRepeatsMonthly,
    decimal? PenaltyMonthCap,
    DateOnly CountsMonthsFrom)
{
    public static readonly BillingPolicy StallTrackDefaults = new(null, 0, true, 3, true, LatePenaltyBasis.None, null, false, null, new DateOnly(2026, 12, 1));

    /// <summary>Whether these rules reach a month: it can hold up later rent, be charged a penalty and be asked of a payor. An earlier month stays on the office's statement and can still be paid.</summary>
    public bool Counts(int year, int month) => new DateOnly(year, month, 1) >= CountsMonthsFrom;

    /// <summary>Whether a month must be paid before later ones. The month in progress always must, even while the start month is still ahead, so next month's rent is never taken over this month's.</summary>
    public bool PaidInOrder(int year, int month, DateOnly today) => Counts(year, month) || new DateOnly(year, month, 1) >= new DateOnly(today.Year, today.Month, 1);

    /// <summary>No due day means the last day of the month, the same day a month becomes Delinquent the morning after (IA-019). A short month clamps.</summary>
    public DateOnly DueDate(int year, int month)
    {
        var last = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, DueDay is { } day ? Math.Min(day, last) : last);
    }

    public bool ChargesPenalty => PenaltyBasis != LatePenaltyBasis.None && PenaltyValue > 0m;

    /// <summary>One penalty charge on what a month still owes. Worked on the rent only, so a penalty never earns a penalty.</summary>
    public decimal PenaltyFor(decimal unpaid) => PenaltyBasis == LatePenaltyBasis.PercentOfUnpaid
        ? decimal.Round(unpaid * PenaltyValue!.Value / 100m, 2, MidpointRounding.AwayFromZero)
        : PenaltyValue ?? 0m;

    /// <summary>The first day a month counts as late: the day after its due date and grace days.</summary>
    public DateOnly FirstLateDay(int year, int month) => DueDate(year, month).AddDays(GraceDays + 1);

    /// <summary>How many penalty charges a month has reached by this date: one once it is late, and one more each month after if the office repeats them.</summary>
    public int ChargesReached(int year, int month, DateOnly today)
    {
        var firstLate = FirstLateDay(year, month);
        if (today < firstLate) return 0;
        if (!PenaltyRepeatsMonthly) return 1;
        var charges = 1;
        while (firstLate.AddMonths(charges) <= today) charges++;
        return charges;
    }

    /// <summary>The months after this one that can be paid ahead, soonest first.</summary>
    public IEnumerable<DateOnly> MonthsPayableAhead(DateOnly today)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        return Enumerable.Range(1, MaxAdvanceMonths).Select(thisMonth.AddMonths);
    }

    /// <summary>Why these rules cannot be kept, or null.</summary>
    public string? Problem()
    {
        if (DueDay is < 1 or > 31) return "The due day must be from 1 to 31.";
        if (GraceDays is < 0 or > 90) return "Grace days must be from 0 to 90.";
        if (MaxAdvanceMonths is < 0 or > 12) return "Months payable ahead must be from 0 to 12.";
        if (CountsMonthsFrom.Year < 2000 || CountsMonthsFrom.Day != 1) return "Choose the month unpaid rent is counted from.";
        if (!Enum.IsDefined(PenaltyBasis)) return "Choose how the late penalty is worked out.";
        if (PenaltyBasis == LatePenaltyBasis.None)
            return PenaltyValue is not null || PenaltyRepeatsMonthly || PenaltyMonthCap is not null ? "Choose how the late penalty is worked out before giving it a rate." : null;
        if (PenaltyValue is not > 0m || decimal.Round(PenaltyValue.Value, 2) != PenaltyValue) return "Enter a positive penalty rate with at most two decimals.";
        if (PenaltyBasis == LatePenaltyBasis.PercentOfUnpaid && PenaltyValue > 100m) return "A percentage penalty cannot exceed 100.";
        if (PenaltyMonthCap is { } cap && (cap <= 0m || decimal.Round(cap, 2) != cap)) return "Enter a positive penalty ceiling with at most two decimals.";
        return null;
    }
}

/// <summary>One version of an office's <see cref="BillingPolicy"/>. Append-only and effective-dated, so a change never reaches back over months already billed.</summary>
public sealed class BillingPolicyVersion : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public int? DueDay { get; private set; }
    public int GraceDays { get; private set; }
    public bool OldestMonthFirst { get; private set; }
    public int MaxAdvanceMonths { get; private set; }
    public bool AnnualPaymentAllowed { get; private set; }
    public LatePenaltyBasis PenaltyBasis { get; private set; }
    public decimal? PenaltyValue { get; private set; }
    public bool PenaltyRepeatsMonthly { get; private set; }
    public decimal? PenaltyMonthCap { get; private set; }
    public DateOnly CountsMonthsFrom { get; private set; }
    public string? GoverningReference { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    private BillingPolicyVersion() { }

    public BillingPolicy Rules => new(DueDay, GraceDays, OldestMonthFirst, MaxAdvanceMonths, AnnualPaymentAllowed,
        PenaltyBasis, PenaltyValue, PenaltyRepeatsMonthly, PenaltyMonthCap, CountsMonthsFrom);

    public static BillingPolicyVersion Create(Guid tenant, DateOnly effectiveFrom, DateOnly today, BillingPolicy rules, string? reference, Guid actor, DateTime now)
    {
        if (tenant == Guid.Empty || actor == Guid.Empty) throw new ArgumentException("A valid office and Head are required.");
        if (effectiveFrom < today) throw new ArgumentException("A billing policy applies from today or later; it cannot reach back over months already billed.");
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required.");
        if (rules.Problem() is { } problem) throw new ArgumentException(problem);
        var governing = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (governing?.Length > 200) throw new ArgumentException("The governing reference must not exceed 200 characters.");
        if (rules.ChargesPenalty && governing is null) throw new ArgumentException("State the ordinance or authority behind the penalty rate.");
        return new()
        {
            MunicipalityId = tenant, EffectiveFrom = effectiveFrom, DueDay = rules.DueDay, GraceDays = rules.GraceDays,
            OldestMonthFirst = rules.OldestMonthFirst, MaxAdvanceMonths = rules.MaxAdvanceMonths, AnnualPaymentAllowed = rules.AnnualPaymentAllowed,
            PenaltyBasis = rules.PenaltyBasis, PenaltyValue = rules.PenaltyValue, PenaltyRepeatsMonthly = rules.PenaltyRepeatsMonthly, PenaltyMonthCap = rules.PenaltyMonthCap,
            CountsMonthsFrom = rules.CountsMonthsFrom, GoverningReference = governing, ActorId = actor, RecordedAtUtc = now
        };
    }

    /// <summary>The version in force on a date: the latest effective, and of those the last recorded. Null means the office has set nothing.</summary>
    public static BillingPolicyVersion? InForce(IEnumerable<BillingPolicyVersion> versions, DateOnly asOf) =>
        versions.Where(v => v.EffectiveFrom <= asOf).OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.RecordedAtUtc).FirstOrDefault();

    /// <summary>The version that governs a billed month's penalty: the one in force on its due date, so a policy never reaches back over a due date already past when it took effect.</summary>
    public static BillingPolicyVersion? GoverningMonth(IReadOnlyCollection<BillingPolicyVersion> versions, int year, int month)
    {
        var latest = InForce(versions, new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        return latest is null ? null : InForce(versions, latest.Rules.DueDate(year, month));
    }
}

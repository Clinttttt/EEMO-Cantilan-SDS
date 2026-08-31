using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Common.Billing
{
    /// <summary>
    /// What a daily-collected market month owes, according to the basis the office has stated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One object rather than a flag tested in thirteen places. Every path that computes a daily-billed month - the stall
    /// ledger and its grid, the closed-accounts register, the settlement service, the settle-month command, the collector's
    /// report, the revenue report, three report handlers and the register - asks THIS, and the two implementations are the
    /// only places either arithmetic is written.
    /// </para>
    /// <para>
    /// The reason it is an object is the fault it prevents. A month measured one way on one screen and another way on the
    /// next is the very thing this platform has already been bitten by: <c>EarnedThrough</c> exists because six paths
    /// disagreed about the month in progress and "one stall carried two different balances depending on which screen the
    /// office opened". A basis threaded as a boolean through thirteen call sites would reproduce that on a bigger scale, so
    /// the basis-less arithmetic is not reachable from outside these implementations at all.
    /// </para>
    /// </remarks>
    public interface IDailyBilledMonthRule
    {
        /// <summary>The basis this rule implements, for a screen that states it back to the office.</summary>
        NpmMonthBasis Basis { get; }

        /// <summary>
        /// Whether a MONTHLY amount means anything on this basis. False on pure days, where no two months owe the same and
        /// a monthly figure would be a number no month actually owes: the screens drop it rather than show a fiction.
        /// </summary>
        bool HasMonthlyGoal { get; }

        /// <summary>
        /// Whether a month whose installments fall short of its rent carries a month-end adjustment for the difference.
        /// False on pure days, where a short month is simply a shorter month.
        /// </summary>
        bool AdjustsShortMonthToRent { get; }

        /// <summary>
        /// What one calendar month OWES for a space held <paramref name="daysHeld"/> of its <paramref name="daysInMonth"/>
        /// days.
        /// </summary>
        /// <param name="dailyFee">The fee this space is billed per market day.</param>
        /// <param name="monthlyRent">The rent the space is let for. Ignored where the basis has no monthly goal.</param>
        decimal Obligation(decimal dailyFee, decimal monthlyRent, int daysInMonth, int daysHeld);

        /// <summary>
        /// The FULL-MONTH reference a space is measured against on a report - the "coverage" column - less the days it was
        /// excused.
        /// </summary>
        decimal Coverage(decimal dailyFee, decimal statedMonthlyRent, int daysInMonth, int excusedDays);
    }

    /// <summary>
    /// A month let for a rent and collected in installments: the platform's original rule, and Cantilan's ordinance.
    /// </summary>
    /// <remarks>
    /// The arithmetic itself stays in <see cref="DomainRules"/>, where it has been tested since before there was a basis to
    /// choose. This type states WHICH arithmetic, and adds nothing to it.
    /// </remarks>
    public sealed class RentGoalMonthRule : IDailyBilledMonthRule
    {
        public NpmMonthBasis Basis => NpmMonthBasis.RentGoal;
        public bool HasMonthlyGoal => true;
        public bool AdjustsShortMonthToRent => true;

        public decimal Obligation(decimal dailyFee, decimal monthlyRent, int daysInMonth, int daysHeld) =>
            DomainRules.DailyBilledMonthObligation(dailyFee, monthlyRent, daysInMonth, daysHeld);

        public decimal Coverage(decimal dailyFee, decimal statedMonthlyRent, int daysInMonth, int excusedDays) =>
            DomainRules.DailyBilledMonthCoverage(dailyFee, statedMonthlyRent, excusedDays);
    }

    /// <summary>
    /// A month that owes the days it has: thirty-one fees in a long month, twenty-eight in February, nothing to adjust.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chosen by the office knowing exactly what it means, which is worth recording because it is money: a 28-day February
    /// owes twenty-eight fees and no month-end top-up, and a year collects 365 installments rather than 360. On this basis
    /// that is not a shortfall, it is the rule.
    /// </para>
    /// <para>
    /// A monthly rent is never consulted here, even when one happens to be stated. An office on this basis is not asked for
    /// one and the screens do not show one, but a figure left behind from an earlier basis must not quietly start deciding
    /// what a month owes.
    /// </para>
    /// </remarks>
    public sealed class PureDaysMonthRule : IDailyBilledMonthRule
    {
        public NpmMonthBasis Basis => NpmMonthBasis.PureDays;
        public bool HasMonthlyGoal => false;
        public bool AdjustsShortMonthToRent => false;

        public decimal Obligation(decimal dailyFee, decimal monthlyRent, int daysInMonth, int daysHeld)
        {
            if (daysHeld <= 0 || daysInMonth <= 0 || dailyFee <= 0m) return 0m;

            // Never more days than the month has: a caller counting an occupancy can hand over a longer span than the
            // calendar, and a month cannot owe thirty-two fees.
            var days = daysHeld > daysInMonth ? daysInMonth : daysHeld;
            return days * dailyFee;
        }

        public decimal Coverage(decimal dailyFee, decimal statedMonthlyRent, int daysInMonth, int excusedDays)
        {
            if (daysInMonth <= 0 || dailyFee <= 0m) return 0m;

            var billable = daysInMonth - (excusedDays < 0 ? 0 : excusedDays);
            return billable <= 0 ? 0m : billable * dailyFee;
        }
    }

    /// <summary>The one place a basis becomes a rule. Both implementations are stateless, so one instance each is enough.</summary>
    public static class DailyBilledMonthRules
    {
        private static readonly IDailyBilledMonthRule RentGoal = new RentGoalMonthRule();
        private static readonly IDailyBilledMonthRule PureDays = new PureDaysMonthRule();

        /// <summary>
        /// The rule for a basis. Anything unrecognised answers with the rent goal, because that is what every office had
        /// before a basis could be stated and a new enum member must never silently re-price a live market.
        /// </summary>
        public static IDailyBilledMonthRule For(NpmMonthBasis basis) =>
            basis == NpmMonthBasis.PureDays ? PureDays : RentGoal;

        /// <summary>The basis an office that has stated nothing is on.</summary>
        public static IDailyBilledMonthRule Default => RentGoal;
    }
}

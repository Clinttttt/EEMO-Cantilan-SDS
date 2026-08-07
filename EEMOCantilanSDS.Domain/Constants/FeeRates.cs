using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Constants
{
    public static class FeeRates
    {
        // NPM — FIXED
        public const decimal NpmDailyFee = 30.00m;   // per day, all sections
        // The reference month for a ₱30 stall: thirty installments, which is what the office's paper states. Kept as
        // the documented figure behind DomainRules.DailyBilledMonthDays; nothing bills from it, because a month is
        // resolved per LGU through Stall.ResolveMonthlyRent.
        public const decimal NpmMonthlyFee = 900.00m;
        public const decimal NpmFishFeePerKilo = 1.00m;    // Fish Area only

        // TCC — RANGE (actual rate from Stall.MonthlyRate)
        public const decimal TccMonthlyMin = 2_400.00m;
        public const decimal TccMonthlyMax = 4_800.00m;

        // NCC — RANGE (actual rate from Stall.MonthlyRate)
        public const decimal NccExtensionMonthly = 1_200.00m; // fixed for Extension area
        public const decimal NccCornerMonthlyMin = 3_240.00m;
        public const decimal NccCornerMonthlyMax = 3_840.00m;

        // BBQ — RANGE (actual rate from Stall.MonthlyRate)
        public const decimal BbqMonthlyMin = 1_600.00m;
        public const decimal BbqMonthlyMax = 9_600.00m;

        // ICE — RANGE (actual rate from Stall.MonthlyRate)
        public const decimal IceMonthlyMin = 1_000.00m;
        public const decimal IceMonthlyMax = 2_000.00m;

        // SLH — FIXED (per head, per animal type)
        public const decimal SlhHogSlaughterFee = 50.00m;
        public const decimal SlhHogAntemortem = 20.00m;
        public const decimal SlhHogTableCharge = 30.00m;
        public const decimal SlhHogEntranceFee = 150.00m;
        public const decimal SlhHogTotalPerHead = 250.00m;  

        public const decimal SlhLargeSlaughterFee = 150.00m;
        public const decimal SlhLargePermit = 100.00m;
        public const decimal SlhLargeAntemortem = 20.00m;
        public const decimal SlhLargePostmortem = 25.00m;
        public const decimal SlhLargeTableCharge = 30.00m;
        public const decimal SlhLargeLivestockFee = 40.00m;
        public const decimal SlhLargeTotalPerHead = 365.00m;

        // TPM — FIXED
        public const decimal TpmVendorFee = 100.00m;  // per vendor per Friday

        // TRM — FIXED
        public const decimal TrmTripFee = 30.00m;  // per trip 
    }
    public static class DomainRules
    {
        public const int PaymentHistoryMonths = 12;
        public const int DelinquentThresholdMonths = 3;
        public const int ExpiringSoonMonths = 3;
        public const int MaxFailedLoginAttempts = 5;
        public const int LockoutMinutes = 15;

        // A daily-collected facility is let for a MONTHLY rent, stated on the office's own List of Stallholders:
        // ₱900 a month and ₱10,800 a year for a ₱30 stall. The daily fee is how that rent is collected — an
        // installment — not what is owed. Calendar length is therefore irrelevant to the obligation: February owes
        // the same ₱900 as August, and twelve complete months owe 12 × ₱900 exactly.
        public const int DailyBilledMonthDays = 30;

        /// <summary>
        /// What a daily-collected space OWES for one calendar month — its canonical contractual obligation.
        ///
        /// <para>A month the space was held in FULL owes the monthly rent, whatever the calendar says: this is the
        /// figure the office's paper states and reconciles against, so a complete year is exactly twelve of them. A
        /// month held only in part — a mid-month start, a term that lapsed, a space taken over — owes the days it was
        /// held, one installment each, and never more than the rent.</para>
        ///
        /// <para><paramref name="monthlyRent"/> is the rent the space is let for (see
        /// <c>Stall.ResolveMonthlyRent</c>): the LGU's own stated market month, or thirty installments when it has
        /// stated none. <paramref name="daysHeld"/> counts every day of the month the space was under this occupancy,
        /// The last day of a billing window that has actually been EARNED as of a given date.
        /// <para>
        /// A daily-billed market space is charged per market day, so no day beyond the as-of date is owed. A month
        /// that has closed keeps its whole window; the month in progress stops at the as-of date; a month entirely in
        /// the future yields a window ending before it starts, which every caller must read as nothing owed.
        /// </para>
        /// <para>
        /// The rule lives here because six paths compute a daily-billed obligation — the stall profile's ledger, its
        /// 12-month grid, the payment dialog's billable months, the reports and arrears engine, the collector's own
        /// report, and the inactive-accounts register — and they disagreed about the month in progress: the profile
        /// stated the days earned while the reports and the collector stated the whole month, so one stall carried two
        /// different balances depending on which screen the office opened.
        /// </para>
        /// <para>Monthly-billed facilities do not use this: their rent falls due when the month opens, by the month.</para>
        /// </summary>
        /// <param name="windowEnd">The last day the window would otherwise cover — a month end, or an occupancy's last billable day.</param>
        /// <param name="asOf">The date the figure is stated as of: today for a live screen, the period end for a closed period.</param>
        public static DateOnly EarnedThrough(DateOnly windowEnd, DateOnly asOf)
            => windowEnd < asOf ? windowEnd : asOf;

        /// <summary>
        /// Whether an occupancy's term has run out as of a given date — the difference between a stall the office is
        /// still letting and one whose lessee has no standing to continue unless the contract is renewed.
        ///
        /// <para>The rule lives here because the arithmetic had been written out by hand in three places — the bulk
        /// import screen, the utility register, and the facility pages via a status flag that does not carry it — and a
        /// facility page consequently held two contradictory ideas of "current" on one screen: a header counting only
        /// stalls with a live contract beside a grid listing every stall not formally closed. The same term was
        /// simultaneously reported as 2 stalls and 5.</para>
        ///
        /// <para>A term of zero years is open-ended — a space let without a contract — and never expires. The last day
        /// of the term is still inside it: a three-year term effective the 7th of June runs THROUGH the 7th of June
        /// three years on, which is the reading the office's own paper takes.</para>
        ///
        /// <para>Expiry is not closure. The lessee is typically still trading and still owes, so an expired term stays
        /// in arrears, in follow-up and in the register of inactive accounts; it is only excluded from the list of
        /// stalls being currently let.</para>
        /// </summary>
        /// <param name="effectivity">The date the term took effect, or null where no contract is on record.</param>
        /// <param name="durationYears">The term length in years; zero or less means open-ended.</param>
        /// <param name="asOf">The date the question is asked as of.</param>
        public static bool TermHasExpired(DateOnly? effectivity, int durationYears, DateOnly asOf)
            => effectivity is { } start && durationYears > 0 && start.AddYears(durationYears) < asOf;

        /// <inheritdoc cref="TermHasExpired(DateOnly?, int, DateOnly)"/>
        public static bool TermHasExpired(DateTime? effectivity, int durationYears, DateTime asOf)
            => TermHasExpired(
                effectivity is { } d ? DateOnly.FromDateTime(d) : null,
                durationYears,
                DateOnly.FromDateTime(asOf));

        /// <summary>
        /// What a daily-collected space OWES for one calendar month — its canonical contractual obligation.
        ///
        /// <para>A month the space was held in FULL owes the monthly rent, whatever the calendar says: this is the
        /// figure the office's paper states and reconciles against, so a complete year is exactly twelve of them. A
        /// month held only in part — a mid-month start, a term that lapsed, a space taken over — owes the days it was
        /// held, one installment each, and never more than the rent.</para>
        ///
        /// <para><paramref name="monthlyRent"/> is the rent the space is let for (see
        /// <c>Stall.ResolveMonthlyRent</c>): the LGU's own stated market month, or thirty installments when it has
        /// stated none. <paramref name="daysHeld"/> counts every day of the month the space was under this occupancy,
        /// including days already collected; <paramref name="daysInMonth"/> is the calendar length.</para>
        /// </summary>
        public static decimal DailyBilledMonthObligation(decimal dailyFee, decimal monthlyRent, int daysInMonth, int daysHeld)
        {
            if (daysHeld <= 0 || daysInMonth <= 0)
                return 0m;

            var rent = monthlyRent > 0m ? monthlyRent : dailyFee * DailyBilledMonthDays;
            if (rent <= 0m)
                return 0m;

            // Held for the whole month → the month's rent, regardless of whether the calendar gave 28 days or 31.
            if (daysHeld >= daysInMonth)
                return rent;

            if (dailyFee <= 0m)
                return 0m;

            var byInstallments = dailyFee * daysHeld;
            return byInstallments < rent ? byInstallments : rent;
        }

        /// <summary>
        /// What is forgiven of a month's obligation: the days the payor owes nothing for — excused/absent days and
        /// facility-wide closures — at one installment each. When every day the space was held is forgiven, the
        /// whole obligation is: a month a payor never traded owes nothing at all, not the rent less its days.
        /// </summary>
        public static decimal DailyBilledMonthCredit(decimal dailyFee, decimal obligation, int daysHeld, int daysForgiven)
        {
            if (dailyFee <= 0m || daysForgiven <= 0 || obligation <= 0m)
                return 0m;

            if (daysForgiven >= daysHeld)
                return obligation;

            var byInstallments = dailyFee * daysForgiven;
            return byInstallments < obligation ? byInstallments : obligation;
        }

        /// <summary>
        /// What is still owed for a month: obligation less what was collected and less what was forgiven. A
        /// collection beyond the obligation is revenue — an over-collection, never a negative debt — so this floors
        /// at nil. Every view that states Expected, Collected, Credits and Outstanding reads these four together,
        /// which is what keeps Expected − Collected − Credits = Outstanding true of the same ledger.
        /// </summary>
        public static decimal DailyBilledMonthOutstanding(decimal obligation, decimal collected, decimal credits)
        {
            var outstanding = obligation - collected - credits;
            return outstanding > 0m ? outstanding : 0m;
        }

        // An occupancy held without a signed contract (a barbecue stand, an ice-plant space, a commercial-centre
        // space on extension) has no term: it runs until the office ends it. Its record carries this length so that
        // nothing treats it as due for renewal or as expired, while the sheets — which ask whether a signed contract
        // exists, never how long this is — print no term for it at all.
        public const int OpenEndedTermYears = 99;

        // Authentication token lifetimes (single source — also used by TokenService).
        public const int AccessTokenMinutes = 15;
        public const int RefreshTokenDays = 7;
    }
}









/*
              // TCC
              public const decimal TccMinMonthly = 2_400.00m;
              public const decimal TccMaxMonthly = 4_800.00m;

              // NCC
              public const decimal NccExtensionMonthly = 1_200.00m;
              public const decimal NccCornerMonthlyMin = 3_240.00m;
              public const decimal NccCornerMonthlyMax = 3_840.00m;

              // BBQ
              public const decimal BbqMinMonthly = 1_600.00m;
              public const decimal BbqMaxMonthly = 9_600.00m;

              // ICE
              public const decimal IceMinMonthly = 1_000.00m;
              public const decimal IceMaxMonthly = 2_000.00m;*/
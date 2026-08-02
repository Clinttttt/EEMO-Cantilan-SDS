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
        public const decimal NpmMonthlyFee = 900.00m;  // 30 days reference
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
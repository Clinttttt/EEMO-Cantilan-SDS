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

        // A daily-collected facility has no monthly contract rate, but the official LGU roster and the
        // ledger still state one: the monthly EQUIVALENT of the daily fee over a flat 30-day month
        // (₱30/day → ₱900/month → ₱10,800/year). Calendar length is deliberately ignored — this is the
        // paper convention the offices reconcile against, not a proration.
        public const int DailyBilledMonthDays = 30;

        /// <summary>
        /// What a daily-collected space OWES for one calendar month: its collectable days at the day's fee, but
        /// never more than the month's base rent (<paramref name="dailyFee"/> × <see cref="DailyBilledMonthDays"/>).
        ///
        /// <para>The office's own paper states ₱900 a month and ₱10,800 a year for a ₱30 stall, so a 31-day month
        /// must not raise a debt of ₱930 against a payor whose rent is ₱900: once the base rent is in, the month is
        /// paid. Collection stays day by day — a 31st day actually traded may still be collected and is real
        /// revenue — but it is income beyond the rent, never an arrear. A month the space held for fewer days than
        /// the base (a mid-month start, excused days) owes only those days, which is why this caps and never
        /// tops up.</para>
        /// </summary>
        public static decimal DailyBilledMonthCharge(decimal dailyFee, int billableDays)
        {
            if (dailyFee <= 0m || billableDays <= 0)
                return 0m;

            var byDays = dailyFee * billableDays;
            var monthlyBase = dailyFee * DailyBilledMonthDays;
            return byDays < monthlyBase ? byDays : monthlyBase;
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
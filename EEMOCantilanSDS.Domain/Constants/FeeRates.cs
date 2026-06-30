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
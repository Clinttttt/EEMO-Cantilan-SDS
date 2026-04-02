using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Constants
{
    public static class FeeRates
    {
        // NPM
        public const decimal NpmDailyFee = 30.00m;
        public const decimal NpmMonthlyFee = 900.00m;
        public const decimal NpmFishFeePerKilo = 1.00m;

      
        // SLH — Hog (₱250/head)
        public const decimal SlhHogSlaughterFee = 50.00m;
        public const decimal SlhHogAntemortem = 20.00m;
        public const decimal SlhHogTableCharge = 30.00m;
        public const decimal SlhHogEntranceFee = 150.00m;
        public const decimal SlhHogTotalPerHead = 250.00m;

        // SLH — Carabao / Cow (₱365/head)
        public const decimal SlhLargeSlaughterFee = 150.00m;
        public const decimal SlhLargePermit = 100.00m;
        public const decimal SlhLargeAntemortem = 20.00m;
        public const decimal SlhLargePostmortem = 25.00m;
        public const decimal SlhLargeTableCharge = 30.00m;
        public const decimal SlhLargeLivestockFee = 40.00m;
        public const decimal SlhLargeTotalPerHead = 365.00m;
    }
    public static class DomainRules
    {
        public const int PaymentHistoryMonths = 12;
        public const int DelinquentThresholdMonths = 3;
        public const int ExpiringSoonMonths = 3;
        public const int MaxFailedLoginAttempts = 5;
        public const int LockoutMinutes = 15;
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
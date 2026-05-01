// Constants matching backend exactly

// Fee Rates - copy from backend FeeRates.cs
export const FeeRates = {
  // NPM (New Public Market)
  NpmDailyFee: 30.00,
  NpmMonthlyFee: 900.00,
  NpmFishFeePerKilo: 1.00,

  // TCC (Tampak Commercial Center)
  TccMonthlyMin: 2400.00,
  TccMonthlyMax: 4800.00,

  // NCC (New Commercial Center)
  NccExtensionMonthly: 1200.00,
  NccCornerMonthlyMin: 3240.00,
  NccCornerMonthlyMax: 3840.00,

  // BBQ (Barbecue Stand)
  BbqMonthlyMin: 1600.00,
  BbqMonthlyMax: 9600.00,

  // ICE (Iceplant)
  IceMonthlyMin: 1000.00,
  IceMonthlyMax: 2000.00,

  // SLH (Slaughterhouse) - Hog
  SlhHogTotalPerHead: 250.00,
  SlhHogSlaughterFee: 50.00,
  SlhHogAntemortem: 20.00,
  SlhHogTableCharge: 30.00,
  SlhHogEntranceFee: 150.00,

  // SLH (Slaughterhouse) - Large Animals (Carabao/Cow)
  SlhLargeTotalPerHead: 365.00,
  SlhLargeSlaughterFee: 150.00,
  SlhLargePermit: 100.00,
  SlhLargeAntemortem: 20.00,
  SlhLargePostmortem: 25.00,
  SlhLargeTableCharge: 30.00,
  SlhLargeLivestockFee: 40.00,
} as const;

// Domain Rules - copy from backend DomainRules.cs
export const DomainRules = {
  PaymentHistoryMonths: 12,
  DelinquentThresholdMonths: 3,
  ExpiringSoonMonths: 3,
  MaxFailedLoginAttempts: 5,
  LockoutMinutes: 15,
} as const;

// Facility Display Names
export const FacilityNames = {
  1: 'New Public Market',
  2: 'Tampak Commercial Center',
  3: 'New Commercial Center',
  4: 'Barbecue Stand',
  5: 'Iceplant',
  6: 'Slaughterhouse',
} as const;

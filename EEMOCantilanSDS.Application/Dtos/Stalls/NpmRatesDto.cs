namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <summary>
/// The current tenant's resolved NPM fixed rates (falls back to the ordinance constants, so Cantilan
/// stays ₱30/day + ₱1/kg). Lets the Add Vendor UI show the LGU's own daily/fish rate instead of a
/// hardcoded ₱30, and compute the daily-equivalent monthly hint — without exposing the fee-rate
/// configuration surface (which is Head-only).
/// </summary>
public sealed record NpmRatesDto(decimal DailyRate, decimal FishRate);

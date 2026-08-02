namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <summary>
/// The current tenant's NPM rates as the billing paths resolve them.
///
/// <para><see cref="MonthlyRent"/> is the rent a market space is let for. It is 0 when the LGU has not stated one, in
/// which case <see cref="MonthlyRentInUse"/> — thirty of the daily fee — is what the system charges until it does.
/// <see cref="NeedsMonthlyRentConfirmation"/> is the portal's cue to ask the office to confirm its ordinance figure:
/// it is never raised for the reference tenant, whose ordinance IS the constants this platform derives from.</para>
/// </summary>
public sealed record NpmRatesDto(
    decimal DailyRate,
    decimal FishRate,
    decimal MonthlyRent = 0m,
    decimal MonthlyRentInUse = 0m,
    bool IsMonthlyRentConfirmed = false,
    bool NeedsMonthlyRentConfirmation = false);

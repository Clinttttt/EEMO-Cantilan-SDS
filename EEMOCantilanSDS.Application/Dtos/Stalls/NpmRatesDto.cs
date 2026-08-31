using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <summary>
/// The current tenant's NPM rates as the billing paths resolve them.
///
/// <para><see cref="MonthlyRent"/> is the rent a market space is let for. It is 0 when the LGU has not stated one, in
/// which case <see cref="MonthlyRentInUse"/> — thirty of the daily fee — is what the system charges until it does.
/// <see cref="NeedsMonthlyRentConfirmation"/> is the portal's cue to ask the office to confirm its ordinance figure:
/// it is never raised for the reference tenant, whose ordinance IS the constants this platform derives from.</para>
///
/// <para>
/// The three per-area figures are what a stall IN THAT AREA is billed: the office's own rate for the area where it prices
/// that area apart, and the market's rate where it does not. They exist because several screens state a rate against an
/// AREA — an import's section picker, for one — and stating the market's figure there told an office that prices its
/// vegetable row at ₱35 that the row costs ₱30. Every one of them equals <see cref="DailyRate"/> for an office that
/// prices nothing apart, which is every office today.
/// </para>
///
/// <para>
/// A stall's OWN rate is deliberately not here and cannot be: it belongs to the stall, and a screen stating one stall's
/// fee asks the server for that stall's resolved figure rather than deriving it from a table of areas.
/// </para>
/// </summary>
public sealed record NpmRatesDto(
    decimal DailyRate,
    decimal FishRate,
    decimal MonthlyRent = 0m,
    decimal MonthlyRentInUse = 0m,
    bool IsMonthlyRentConfirmed = false,
    bool NeedsMonthlyRentConfirmation = false,
    decimal VegetableAreaDailyRate = 0m,
    decimal FishSectionDailyRate = 0m,
    decimal MeatSectionDailyRate = 0m,
    /// <summary>
    /// How this office measures what a market month owes: a monthly goal collected in installments, or the days the month
    /// actually has.
    /// </summary>
    /// <remarks>
    /// Stated back to the office so a screen can say which rule is in force, and so a form knows whether a MONTHLY field
    /// belongs on it at all. On the days basis no monthly amount is meaningful, because no two months owe the same.
    /// </remarks>
    NpmMonthBasis MonthBasis = NpmMonthBasis.RentGoal,
    /// <summary>
    /// True where this office has never STATED how it measures a market month, so the console asks it once.
    /// </summary>
    /// <remarks>
    /// Asked before the office records vendors, because the answer decides what every month those vendors owe adds up to.
    /// The reference tenant is never asked: its own ordinance is the convention this platform's constants come from.
    /// </remarks>
    bool NeedsMonthRuleConfirmation = false);

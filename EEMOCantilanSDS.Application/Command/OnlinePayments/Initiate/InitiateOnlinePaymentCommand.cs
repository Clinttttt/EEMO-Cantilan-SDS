using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;

/// <summary>
/// Payor initiates an online payment for one billing month of a stall. The monthly record is found or
/// created at initiation (so a current-month obligation with no record yet is payable). Full balance only.
/// <see cref="Kind"/> disambiguates NPM's payable items (daily fees vs the utility bill vs a fish day) for
/// the same stall + month; monthly-rental facilities ignore it. <see cref="Day"/> + <see cref="FishKilos"/>
/// are used only by the NPM fish-day kind (pay ONE day, self-declaring that day's kilos).
/// </summary>
/// <param name="Days">
/// How MANY of the month's owed days to pay, oldest first, for a daily-billed space. Absent means the month's whole
/// outstanding balance, which is what every caller sent before this existed and remains the default.
///
/// <para>
/// A count and not a set of dates, deliberately. The office settles a daily-billed month oldest first, and its own
/// settlement walks the month in that order, so a payor who could pay a later day while leaving an earlier one open
/// would be creating an arrear behind a paid day. The count says how far down that same list the money reaches, which
/// is the one thing the payor gets to choose. The amount is always the office's, computed here from its own rates.
/// </para>
/// </param>
/// <param name="FishDays">
/// The fish-section days this payment covers and the kilos the payor declared for EACH of them. A fish day costs the
/// stall's daily fee plus that day's own weighing fee, so several days cannot be paid as one figure and a count: the
/// office would be marking days with a weight nobody declared. Kilos may be left out, which means none were sold, and
/// that is what an office with no per-kilo rate charges for anyway. One entry is the day-at-a-time path, unchanged.
/// </param>
public record InitiateOnlinePaymentCommand(Guid StallId, int Year, int Month, PayorPayableKind Kind = PayorPayableKind.Monthly, int? Day = null, decimal? FishKilos = null, int? Days = null, IReadOnlyList<FishDayDeclarationInput>? FishDays = null) : IRequest<Result<InitiateOnlinePaymentResultDto>>;

/// <summary>One fish day the payor is paying for, and the kilos they declared for it. Kilos absent means none were sold.</summary>
public record FishDayDeclarationInput(int Day, decimal? Kilos);

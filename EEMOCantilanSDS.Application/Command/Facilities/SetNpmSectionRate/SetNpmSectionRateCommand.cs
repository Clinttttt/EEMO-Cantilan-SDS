using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionRate;

/// <summary>
/// States the daily fee for one of the office's OWN market sections (Head-only).
///
/// <para>
/// Effective today and never backwards: every elapsed day keeps the fee it was owed at, which is the office's own ruling
/// and the reason these rows are effective-dated at all. A stall let at its own rate keeps that rate; the section's figure
/// answers for the stalls that carry none.
/// </para>
/// </summary>
/// <param name="DailyRate">
/// The fee for one day. Nought withdraws the figure and returns the section's stalls to the market's own rate, the same
/// reading a cleared area rate has: an ordinance does not let a market space for nothing.
/// </param>
public record SetNpmSectionRateCommand(string Section, decimal DailyRate) : IRequest<Result<bool>>;

using System;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.SetMarketDay
{
    /// <summary>
    /// Moves the office's weekly market to a different weekday, from <paramref name="EffectiveFrom"/> onwards.
    ///
    /// <para>
    /// Effective-dated rather than an overwrite, for the same reason a fee rate is: the weeks the office has
    /// already collected were held on the old day, and a change that reached backwards would put every one of
    /// them on a day its own system says was not a market day. So the new day starts on a date the office names,
    /// never earlier than today, and that date must itself fall on the new day — the first market day under the
    /// new arrangement.
    /// </para>
    /// </summary>
    public record SetTpmMarketDayCommand(DayOfWeek Day, DateOnly EffectiveFrom) : IRequest<Result<bool>>;
}

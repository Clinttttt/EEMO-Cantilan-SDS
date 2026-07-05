using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate
{
    /// <summary>
    /// Lets an LGU Head adjust one of their own fixed ordinance rates (self-service, post-activation). The
    /// change is recorded as an effective-dated <c>FacilityRate</c> row for the caller's municipality, taking
    /// effect from today forward — so already-elapsed periods (and every other LGU) are never affected. The
    /// resolver then serves the new amount for current/future dates and the prior amount for past dates.
    /// </summary>
    public record SetFacilityRateCommand(FacilityCode FacilityCode, FeeRateKey Key, decimal Amount)
        : IRequest<Result<bool>>;
}

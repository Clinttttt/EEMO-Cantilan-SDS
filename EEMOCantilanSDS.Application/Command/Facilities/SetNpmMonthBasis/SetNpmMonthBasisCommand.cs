using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmMonthBasis;

/// <summary>
/// States how this office measures what a daily-collected market month owes.
/// </summary>
/// <param name="Basis">
/// <see cref="NpmMonthBasis.RentGoal"/> - the month is let for a rent and collected in installments - or
/// <see cref="NpmMonthBasis.PureDays"/>, where the month owes the days it has.
/// </param>
public record SetNpmMonthBasisCommand(NpmMonthBasis Basis) : IRequest<Result<bool>>;

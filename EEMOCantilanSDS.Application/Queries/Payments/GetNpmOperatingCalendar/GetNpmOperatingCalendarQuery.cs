using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetNpmOperatingCalendar;

/// <summary>One month of the New Public Market's effective operating calendar, with the reason for each day's state.</summary>
public record GetNpmOperatingCalendarQuery(int Year, int Month) : IRequest<Result<NpmOperatingCalendarDto>>;

/// <summary>What opening or closing one day would change, before it is confirmed.</summary>
public record GetNpmDayImpactQuery(DateOnly Date) : IRequest<Result<NpmDayImpactDto>>;

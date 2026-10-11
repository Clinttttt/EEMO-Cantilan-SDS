using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetNpmOperatingCalendar;

public class GetNpmOperatingCalendarQueryHandler(INpmOperatingScheduleRepository repository)
    : IRequestHandler<GetNpmOperatingCalendarQuery, Result<NpmOperatingCalendarDto>>
{
    public async Task<Result<NpmOperatingCalendarDto>> Handle(GetNpmOperatingCalendarQuery request, CancellationToken ct)
    {
        if (request.Month is < 1 or > 12 || request.Year is < 2000 or > 2100)
            return Result<NpmOperatingCalendarDto>.Failure("Choose a valid month.", ResultStatus.Invalid);

        var start = new DateOnly(request.Year, request.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var schedules = await repository.GetAllAsync(ct);
        var rows = (await repository.GetRowsAsync(start, end, ct)).GroupBy(r => r.ClosureDate).ToDictionary(g => g.Key, g => g.First());

        var days = new List<NpmOperatingDayDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            rows.TryGetValue(day, out var row);
            days.Add(new NpmOperatingDayDto(day, NpmOperatingCalendar.StateOf(day, schedules, row),
                row is { IsOpenOverride: false } ? row.Reason.ToString() : null, row?.Remarks, row is not null));
        }

        static NpmOperatingScheduleDto Map(NpmOperatingSchedule s) => new(s.EffectiveFrom, s.ClosedDays, s.Remarks, s.CreatedBy);
        var inForce = NpmOperatingCalendar.InForce(end, schedules);
        var closed = days.Count(d => NpmOperatingCalendar.IsClosed(d.State));
        return Result<NpmOperatingCalendarDto>.Success(new NpmOperatingCalendarDto(
            request.Year, request.Month, days, schedules.Select(Map).ToList(), inForce is null ? null : Map(inForce), closed, days.Count - closed));
    }
}

public class GetNpmDayImpactQueryHandler(INpmOperatingScheduleRepository repository, IClock clock)
    : IRequestHandler<GetNpmDayImpactQuery, Result<NpmDayImpactDto>>
{
    public async Task<Result<NpmDayImpactDto>> Handle(GetNpmDayImpactQuery request, CancellationToken ct)
    {
        var impact = await repository.GetDayImpactAsync(request.Date, ct);
        return Result<NpmDayImpactDto>.Success(new NpmDayImpactDto(
            request.Date, request.Date < clock.PhilippineToday, impact.BillableAccounts, impact.UnpaidAccounts, impact.PaidAccounts, impact.UnpaidAmount));
    }
}

using FluentValidation;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;

public class GetFacilityReportsQueryValidator : AbstractValidator<GetFacilityReportsQuery>
{
    public GetFacilityReportsQueryValidator()
    {
        RuleFor(x => x.FacilityCode)
            .IsInEnum()
            .WithMessage("Invalid facility code");
        
        RuleFor(x => x.Period)
            .IsInEnum()
            .WithMessage("Period must be Weekly, Monthly, or Yearly");
        
        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(PhilippineTime.Today.Year + 1)
            .WithMessage("Year must be between 2000 and next year");
        
        // Weekly requires month and weekNumber
        When(x => x.Period == ReportPeriod.Weekly, () =>
        {
            RuleFor(x => x.Month)
                .NotNull()
                .InclusiveBetween(1, 12)
                .WithMessage("Month is required for weekly reports (1-12)");
            
            RuleFor(x => x.WeekNumber)
                .NotNull()
                .InclusiveBetween(1, 5)
                .WithMessage("Week number is required for weekly reports (1-5)");

            // A month can have 4 or 5 weeks (e.g. a 28-day February has only 4),
            // so reject weeks that don't exist in the selected month/year.
            RuleFor(x => x)
                .Must(WeekExistsInMonth)
                .WithMessage("The selected week does not exist in the chosen month");
        });
        
        // Monthly requires month
        When(x => x.Period == ReportPeriod.Monthly, () =>
        {
            RuleFor(x => x.Month)
                .NotNull()
                .InclusiveBetween(1, 12)
                .WithMessage("Month is required for monthly reports (1-12)");
        });
    }

    private static bool WeekExistsInMonth(GetFacilityReportsQuery query)
    {
        // Defer to the dedicated rules when month/week/year are out of range.
        if (query.WeekNumber is null || query.Month is not (>= 1 and <= 12) || query.Year is < 1 or > 9999)
            return true;

        var weeksInMonth = (DateTime.DaysInMonth(query.Year, query.Month.Value) + 6) / 7; // ceil(days / 7)
        return query.WeekNumber.Value <= weeksInMonth;
    }
}

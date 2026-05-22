using FluentValidation;
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
            .LessThanOrEqualTo(DateTime.UtcNow.Year + 1)
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
}

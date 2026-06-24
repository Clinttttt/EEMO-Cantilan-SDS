using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;

public class GetFinancialReportQueryValidator : AbstractValidator<GetFinancialReportQuery>
{
    public GetFinancialReportQueryValidator()
    {
        RuleFor(x => x.Period)
            .IsInEnum()
            .WithMessage("Period must be Monthly or Yearly");

        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(PhilippineTime.Today.Year + 1)
            .WithMessage("Year must be between 2000 and next year");

        // Facility is optional (null = all facilities); validate only when supplied.
        RuleFor(x => x.Facility!.Value)
            .IsInEnum()
            .When(x => x.Facility.HasValue)
            .WithMessage("Invalid facility code");

        When(x => x.Period == ReportPeriod.Monthly, () =>
        {
            RuleFor(x => x.Month)
                .NotNull()
                .InclusiveBetween(1, 12)
                .WithMessage("Month is required for monthly reports (1-12)");
        });
    }
}

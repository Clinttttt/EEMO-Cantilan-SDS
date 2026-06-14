using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetMonthEndReport;

public class GetMonthEndReportQueryValidator : AbstractValidator<GetMonthEndReportQuery>
{
    public GetMonthEndReportQueryValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100)
            .WithMessage("Year must be a valid reporting year.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12)
            .WithMessage("Month must be between 1 and 12.");
    }
}

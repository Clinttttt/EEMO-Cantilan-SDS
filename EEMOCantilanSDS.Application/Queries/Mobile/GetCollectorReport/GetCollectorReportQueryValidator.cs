using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;

public class GetCollectorReportQueryValidator : AbstractValidator<GetCollectorReportQuery>
{
    public GetCollectorReportQueryValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100);

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12);
    }
}

using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetCollectionReport;

public class GetCollectionReportQueryValidator : AbstractValidator<GetCollectionReportQuery>
{
    public GetCollectionReportQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

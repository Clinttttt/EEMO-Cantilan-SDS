using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistory;

public class GetFollowUpHistoryQueryValidator : AbstractValidator<GetFollowUpHistoryQuery>
{
    public GetFollowUpHistoryQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

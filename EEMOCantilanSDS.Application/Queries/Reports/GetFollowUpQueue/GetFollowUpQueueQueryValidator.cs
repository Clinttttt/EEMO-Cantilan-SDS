using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;

public class GetFollowUpQueueQueryValidator : AbstractValidator<GetFollowUpQueueQuery>
{
    public GetFollowUpQueueQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

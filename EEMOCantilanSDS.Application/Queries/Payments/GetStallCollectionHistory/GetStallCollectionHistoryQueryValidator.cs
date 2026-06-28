using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallCollectionHistory;

public class GetStallCollectionHistoryQueryValidator : AbstractValidator<GetStallCollectionHistoryQuery>
{
    public GetStallCollectionHistoryQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

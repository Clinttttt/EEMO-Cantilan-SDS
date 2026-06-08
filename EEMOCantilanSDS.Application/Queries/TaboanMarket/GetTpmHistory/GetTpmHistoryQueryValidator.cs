using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmHistory;

public class GetTpmHistoryQueryValidator : AbstractValidator<GetTpmHistoryQuery>
{
    public GetTpmHistoryQueryValidator()
    {
        RuleFor(x => x.Year)
            .GreaterThan(2000).WithMessage("Year must be greater than 2000.");
    }
}

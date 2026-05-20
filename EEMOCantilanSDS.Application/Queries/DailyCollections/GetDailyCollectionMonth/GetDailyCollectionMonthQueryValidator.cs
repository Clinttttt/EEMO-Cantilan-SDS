using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;

public class GetDailyCollectionMonthQueryValidator : AbstractValidator<GetDailyCollectionMonthQuery>
{
    public GetDailyCollectionMonthQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Year).GreaterThan(2000).LessThanOrEqualTo(2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

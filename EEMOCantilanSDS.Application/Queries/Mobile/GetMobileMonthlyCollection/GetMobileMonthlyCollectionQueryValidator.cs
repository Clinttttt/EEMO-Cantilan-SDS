using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;

public sealed class GetMobileMonthlyCollectionQueryValidator : AbstractValidator<GetMobileMonthlyCollectionQuery>
{
    public GetMobileMonthlyCollectionQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000).LessThanOrEqualTo(2100);
        RuleFor(x => x.Month).GreaterThan(0).LessThanOrEqualTo(12);
    }
}

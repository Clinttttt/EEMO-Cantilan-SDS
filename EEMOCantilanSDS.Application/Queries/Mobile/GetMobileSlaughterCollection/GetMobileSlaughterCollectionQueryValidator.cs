using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileSlaughterCollection;

public sealed class GetMobileSlaughterCollectionQueryValidator : AbstractValidator<GetMobileSlaughterCollectionQuery>
{
    public GetMobileSlaughterCollectionQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000).LessThanOrEqualTo(2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Day).InclusiveBetween(1, 31);
    }
}

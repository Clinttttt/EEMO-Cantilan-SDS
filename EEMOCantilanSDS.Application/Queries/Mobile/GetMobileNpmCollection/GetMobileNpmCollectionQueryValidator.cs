using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;

public sealed class GetMobileNpmCollectionQueryValidator : AbstractValidator<GetMobileNpmCollectionQuery>
{
    public GetMobileNpmCollectionQueryValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100);

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12);
    }
}

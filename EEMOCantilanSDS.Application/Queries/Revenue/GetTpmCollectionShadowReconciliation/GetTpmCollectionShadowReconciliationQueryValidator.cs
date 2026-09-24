using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetTpmCollectionShadowReconciliation;

public sealed class GetTpmCollectionShadowReconciliationQueryValidator
    : AbstractValidator<GetTpmCollectionShadowReconciliationQuery>
{
    public GetTpmCollectionShadowReconciliationQueryValidator() =>
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("To date must be on or after From date.");
}

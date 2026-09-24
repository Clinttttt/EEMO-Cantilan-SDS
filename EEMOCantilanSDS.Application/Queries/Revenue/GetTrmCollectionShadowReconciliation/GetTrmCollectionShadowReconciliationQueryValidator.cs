using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetTrmCollectionShadowReconciliation;

public sealed class GetTrmCollectionShadowReconciliationQueryValidator
    : AbstractValidator<GetTrmCollectionShadowReconciliationQuery>
{
    public GetTrmCollectionShadowReconciliationQueryValidator() =>
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("To date must be on or after From date.");
}

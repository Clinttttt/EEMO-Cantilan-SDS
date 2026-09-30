using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetNpmWeighingShadowReconciliation;

public sealed class GetNpmWeighingShadowReconciliationQueryValidator
    : AbstractValidator<GetNpmWeighingShadowReconciliationQuery>
{
    public GetNpmWeighingShadowReconciliationQueryValidator() =>
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("To date must be on or after From date.");
}

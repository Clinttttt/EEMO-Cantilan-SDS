using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassificationPolicyHistory;

public sealed class GetRevenueClassificationPolicyHistoryQueryValidator
    : AbstractValidator<GetRevenueClassificationPolicyHistoryQuery>
{
    public GetRevenueClassificationPolicyHistoryQueryValidator() =>
        RuleFor(x => x.ClassificationId).NotEmpty();
}

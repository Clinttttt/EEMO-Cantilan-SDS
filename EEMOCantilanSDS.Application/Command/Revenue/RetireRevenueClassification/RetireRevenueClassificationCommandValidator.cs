using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Revenue.RetireRevenueClassification;

public sealed class RetireRevenueClassificationCommandValidator
    : AbstractValidator<RetireRevenueClassificationCommand>
{
    public RetireRevenueClassificationCommandValidator() => RuleFor(x => x.ClassificationId).NotEmpty();
}

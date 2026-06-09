using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(x => x.StallId)
            .NotEmpty();

        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(2100);

        RuleFor(x => x.Month)
            .GreaterThan(0)
            .LessThanOrEqualTo(12);

        RuleFor(x => x.Status)
            .IsInEnum();

        RuleFor(x => x.PartialAmount)
            .GreaterThan(0)
            .When(x => x.Status == PaymentStatus.Partial)
            .WithMessage("Partial amount must be greater than 0 when status is Partial");

        RuleFor(x => x.ORNumber)
            .MaximumLength(30)
            .When(x => !string.IsNullOrWhiteSpace(x.ORNumber));
    }
}

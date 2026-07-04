using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;

public class RecordUtilityPaymentCommandValidator : AbstractValidator<RecordUtilityPaymentCommand>
{
    public RecordUtilityPaymentCommandValidator()
    {
        RuleFor(x => x.BillId).NotEmpty();

        RuleFor(x => x.ElecPartialAmount)
            .GreaterThan(0)
            .When(x => x.ElecStatus == PaymentStatus.Partial)
            .WithMessage("Enter an electricity partial amount greater than 0.");

        RuleFor(x => x.WaterPartialAmount)
            .GreaterThan(0)
            .When(x => x.WaterStatus == PaymentStatus.Partial)
            .WithMessage("Enter a water partial amount greater than 0.");

        RuleFor(x => x.ElecPartialAmount).GreaterThanOrEqualTo(0).When(x => x.ElecPartialAmount.HasValue);
        RuleFor(x => x.WaterPartialAmount).GreaterThanOrEqualTo(0).When(x => x.WaterPartialAmount.HasValue);

        RuleFor(x => x.ElecORNumber).MaximumLength(50);
        RuleFor(x => x.WaterORNumber).MaximumLength(50);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

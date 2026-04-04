using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;

public class SaveOrNumberCommandValidator : AbstractValidator<SaveOrNumberCommand>
{
    public SaveOrNumberCommandValidator(IPaymentRepository paymentRepository)
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.ORNumber)
            .NotEmpty()
            .MaximumLength(50)
            .MustAsync(async (orNumber, ct) => await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
            .WithMessage("OR Number already exists");
    }
}

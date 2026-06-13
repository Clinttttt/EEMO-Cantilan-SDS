using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;

public class IssueOnlinePaymentOrNumberCommandValidator : AbstractValidator<IssueOnlinePaymentOrNumberCommand>
{
    public IssueOnlinePaymentOrNumberCommandValidator(IPaymentRepository paymentRepository)
    {
        RuleFor(x => x.TransactionId).NotEmpty();

        RuleFor(x => x.ORNumber)
            .NotEmpty()
            .MaximumLength(50)
            .MustAsync(async (orNumber, ct) => await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
            .WithMessage("OR Number already exists");
    }
}

using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;

public class IssueOnlinePaymentOrNumberCommandValidator : AbstractValidator<IssueOnlinePaymentOrNumberCommand>
{
    public IssueOnlinePaymentOrNumberCommandValidator(IOrNumberRegistry orNumbers)
    {
        RuleFor(x => x.TransactionId).NotEmpty();

        RuleFor(x => x.ORNumber)
            .NotEmpty()
            .MaximumLength(50)
            .MustAsync(async (orNumber, ct) => await orNumbers.IsAvailableAsync(orNumber, ct))
            .WithMessage("OR Number already exists");
    }
}

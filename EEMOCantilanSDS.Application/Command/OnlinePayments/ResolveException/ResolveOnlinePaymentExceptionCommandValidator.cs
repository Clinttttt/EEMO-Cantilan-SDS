using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.ResolveException;

public class ResolveOnlinePaymentExceptionCommandValidator : AbstractValidator<ResolveOnlinePaymentExceptionCommand>
{
    public ResolveOnlinePaymentExceptionCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.Disposition).IsInEnum();
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

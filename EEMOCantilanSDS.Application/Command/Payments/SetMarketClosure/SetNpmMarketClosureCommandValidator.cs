using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;

public class SetNpmMarketClosureCommandValidator : AbstractValidator<SetNpmMarketClosureCommand>
{
    public SetNpmMarketClosureCommandValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(300);
    }
}

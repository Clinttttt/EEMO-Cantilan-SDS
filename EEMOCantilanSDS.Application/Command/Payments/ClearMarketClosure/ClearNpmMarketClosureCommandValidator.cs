using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;

public class ClearNpmMarketClosureCommandValidator : AbstractValidator<ClearNpmMarketClosureCommand>
{
    public ClearNpmMarketClosureCommandValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
    }
}

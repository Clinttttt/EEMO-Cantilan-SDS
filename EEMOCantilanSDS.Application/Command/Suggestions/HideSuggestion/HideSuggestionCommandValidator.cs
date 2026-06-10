using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Suggestions.HideSuggestion;

public class HideSuggestionCommandValidator : AbstractValidator<HideSuggestionCommand>
{
    public HideSuggestionCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Value is required.")
            .MaximumLength(150);
    }
}

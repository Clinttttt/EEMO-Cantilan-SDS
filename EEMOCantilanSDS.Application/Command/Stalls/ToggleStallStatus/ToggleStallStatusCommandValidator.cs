using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;

public class ToggleStallStatusCommandValidator : AbstractValidator<ToggleStallStatusCommand>
{
    public ToggleStallStatusCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
    }
}

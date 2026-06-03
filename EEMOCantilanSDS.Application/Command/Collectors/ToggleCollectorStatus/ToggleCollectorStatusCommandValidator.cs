using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.ToggleCollectorStatus;

public class ToggleCollectorStatusCommandValidator : AbstractValidator<ToggleCollectorStatusCommand>
{
    public ToggleCollectorStatusCommandValidator()
    {
        RuleFor(x => x.CollectorId).NotEmpty();
    }
}

using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Backup.TriggerRestore;

public class TriggerRestoreCommandValidator : AbstractValidator<TriggerRestoreCommand>
{
    public TriggerRestoreCommandValidator()
    {
        RuleFor(x => x.ConfirmationPhrase)
            .NotEmpty();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}

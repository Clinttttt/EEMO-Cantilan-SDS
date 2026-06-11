using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Admins.ToggleAdminStatus;

public class ToggleAdminStatusCommandValidator : AbstractValidator<ToggleAdminStatusCommand>
{
    public ToggleAdminStatusCommandValidator()
    {
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

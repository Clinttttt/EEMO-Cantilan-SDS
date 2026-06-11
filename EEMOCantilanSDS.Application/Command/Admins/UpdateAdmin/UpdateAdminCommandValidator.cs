using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;

public class UpdateAdminCommandValidator : AbstractValidator<UpdateAdminCommand>
{
    public UpdateAdminCommandValidator()
    {
        RuleFor(x => x.AdminId).NotEmpty();

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(100).WithMessage("Full name cannot exceed 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid admin role");
    }
}

using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;

public class InviteAdminCommandValidator : AbstractValidator<InviteAdminCommand>
{
    public InviteAdminCommandValidator(IAdminRepository adminRepo)
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Full name is required")
            .MaximumLength(100).WithMessage("Full name cannot exceed 100 characters");

        // The invited person may choose a different username when they accept; this one only has to be free now.
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required")
            .MinimumLength(4).WithMessage("Username must be at least 4 characters")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters")
            .MustAsync((u, ct) => adminRepo.IsUsernameUniqueAsync(u, ct)).WithMessage("Username already exists");

        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MustAsync((e, ct) => adminRepo.IsEmailUniqueAsync(e, ct)).WithMessage("Email already exists");

        RuleFor(x => x.Role).IsInEnum().WithMessage("Invalid admin role");
    }
}

using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;

public class CreateAdminCommandValidator : AbstractValidator<CreateAdminCommand>
{
    private readonly IAdminRepository _adminRepo;

    public CreateAdminCommandValidator(IAdminRepository adminRepo)
    {
        _adminRepo = adminRepo;

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(100).WithMessage("Full name cannot exceed 100 characters");

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MinimumLength(4).WithMessage("Username must be at least 4 characters")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters")
            .MustAsync(BeUniqueUsername).WithMessage("Username already exists");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MustAsync(BeUniqueEmail).WithMessage("Email already exists");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid admin role");
    }

    private async Task<bool> BeUniqueUsername(string username, CancellationToken cancellationToken)
        => await _adminRepo.IsUsernameUniqueAsync(username, cancellationToken);

    private async Task<bool> BeUniqueEmail(string email, CancellationToken cancellationToken)
        => await _adminRepo.IsEmailUniqueAsync(email, cancellationToken);
}

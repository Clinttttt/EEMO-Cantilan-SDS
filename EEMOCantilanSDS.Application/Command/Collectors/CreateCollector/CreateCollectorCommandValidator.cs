using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;

public class CreateCollectorCommandValidator : AbstractValidator<CreateCollectorCommand>
{
    private readonly ICollectorRepository _collectorRepo;

    public CreateCollectorCommandValidator(ICollectorRepository collectorRepo)
    {
        _collectorRepo = collectorRepo;

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(100).WithMessage("Full name cannot exceed 100 characters");

        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Employee ID is required")
            .MaximumLength(50).WithMessage("Employee ID cannot exceed 50 characters")
            .MustAsync(BeUniqueEmployeeId).WithMessage("Employee ID already exists");

        RuleFor(x => x.ContactNumber)
            .NotEmpty().WithMessage("Contact number is required")
            .Matches(@"^\+63\s?\d{3}\s?\d{3}\s?\d{4}$").WithMessage("Contact number must be a valid Philippine number");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MustAsync(BeUniqueEmail).WithMessage("Email already exists");

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MinimumLength(4).WithMessage("Username must be at least 4 characters")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters")
            .MustAsync(BeUniqueUsername).WithMessage("Username already exists");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");

        RuleFor(x => x.AssignedFacilities)
            .NotEmpty().WithMessage("At least one facility must be assigned");
    }

    private async Task<bool> BeUniqueEmployeeId(string employeeId, CancellationToken cancellationToken)
    {
        return await _collectorRepo.IsEmployeeIdUniqueAsync(employeeId, cancellationToken);
    }

    private async Task<bool> BeUniqueEmail(string email, CancellationToken cancellationToken)
    {
        return await _collectorRepo.IsEmailUniqueAsync(email, cancellationToken);
    }

    private async Task<bool> BeUniqueUsername(string username, CancellationToken cancellationToken)
    {
        return await _collectorRepo.IsUsernameUniqueAsync(username, cancellationToken);
    }
}

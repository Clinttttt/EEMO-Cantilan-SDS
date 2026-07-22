using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;

public class UpdateCollectorCommandValidator : AbstractValidator<UpdateCollectorCommand>
{
    public UpdateCollectorCommandValidator()
    {
        RuleFor(x => x.CollectorId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.AssignedFacilities).NotEmpty();

        // Username is optional on update (blank = keep current); when supplied it must be a valid login.
        RuleFor(x => x.Username)
            .MinimumLength(4).WithMessage("Username must be at least 4 characters")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
    }
}

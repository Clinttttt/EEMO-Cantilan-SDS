using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;

/// <summary>
/// Guards only what this command itself decides, and mirrors the limits the create path enforces so the office is
/// told about a bad figure here rather than through a validation message from a nested command. The new stall's
/// number is checked for uniqueness by that create path, so the rule is not duplicated.
/// </summary>
public class AssignPastOccupantStallCommandValidator : AbstractValidator<AssignPastOccupantStallCommand>
{
    public AssignPastOccupantStallCommandValidator()
    {
        RuleFor(x => x.PreviousStallId).NotEmpty();
        RuleFor(x => x.StallNo)
            .NotEmpty().WithMessage("Give the new stall a number.")
            .MaximumLength(20).WithMessage("Stall number cannot exceed 20 characters");
        RuleFor(x => x.ContractYears)
            .InclusiveBetween(1, 10).WithMessage("Contract duration must be between 1 and 10 years.");
        RuleFor(x => x.MonthlyRate)
            .GreaterThan(0).WithMessage("Enter the monthly rate for the new stall.");
    }
}

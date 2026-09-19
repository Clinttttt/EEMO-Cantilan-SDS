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
        // Whether zero is allowed is a facility rule, and the nested CreateStallCommand is the authoritative path that
        // knows the facility and its tenant-resolved MonthBasis. This outer validator guards only the structural floor;
        // keeping a second unconditional positive check here refused every valid PureDays reassignment.
        RuleFor(x => x.MonthlyRate)
            .GreaterThanOrEqualTo(0).WithMessage("Monthly rate cannot be negative.");
    }
}

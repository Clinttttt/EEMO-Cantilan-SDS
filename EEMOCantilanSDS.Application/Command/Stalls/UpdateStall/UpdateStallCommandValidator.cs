using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;

public class UpdateStallCommandValidator : AbstractValidator<UpdateStallCommand>
{
    public UpdateStallCommandValidator()
    {
        RuleFor(x => x.StallId)
            .NotEmpty().WithMessage("Stall ID is required");

        RuleFor(x => x.MonthlyRate)
            .GreaterThan(0).WithMessage("Monthly rate must be greater than ₱0");

        RuleFor(x => x.ActualOccupant)
            .NotEmpty().WithMessage("Actual occupant is required")
            .MaximumLength(200).WithMessage("Actual occupant name cannot exceed 200 characters");

        RuleFor(x => x.DailyRate)
            .GreaterThan(0).WithMessage("Daily rate must be greater than ₱0")
            .When(x => x.DailyRate.HasValue);

        RuleFor(x => x.ContractYears)
            .InclusiveBetween(0, 50).WithMessage("Contract duration must be between 0 and 50 years")
            .When(x => x.ContractYears.HasValue);
    }
}

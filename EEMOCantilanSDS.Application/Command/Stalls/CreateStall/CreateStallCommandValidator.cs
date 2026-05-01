using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.CreateStall;

public class CreateStallCommandValidator : AbstractValidator<CreateStallCommand>
{
    private readonly IStallRepository _stallRepo;

    public CreateStallCommandValidator(IStallRepository stallRepo)
    {
        _stallRepo = stallRepo;

        RuleFor(x => x.StallNo)
            .NotEmpty().WithMessage("Stall number is required")
            .MaximumLength(20).WithMessage("Stall number cannot exceed 20 characters");

        RuleFor(x => x.StallNo)
            .MustAsync(BeUniqueStallNo).WithMessage("Stall number already exists in this facility")
            .When(x => !string.IsNullOrWhiteSpace(x.StallNo));

        RuleFor(x => x.MonthlyRate)
            .GreaterThan(0).WithMessage("Monthly rate must be greater than ₱0");

        RuleFor(x => x.ActualOccupant)
            .NotEmpty().WithMessage("Actual occupant is required")
            .MaximumLength(200).WithMessage("Actual occupant name cannot exceed 200 characters");

        RuleFor(x => x.ContractYears)
            .GreaterThan(0).WithMessage("Contract duration must be at least 1 year")
            .LessThanOrEqualTo(10).WithMessage("Contract duration cannot exceed 10 years");

        RuleFor(x => x.Section)
            .NotNull().WithMessage("Section is required for NPM")
            .When(x => x.FacilityCode == FacilityCode.NPM);

        RuleFor(x => x.DailyRate)
            .NotNull().WithMessage("Daily rate is required for NPM")
            .GreaterThan(0).WithMessage("Daily rate must be greater than ₱0")
            .When(x => x.FacilityCode == FacilityCode.NPM);
    }

    private async Task<bool> BeUniqueStallNo(CreateStallCommand command, string stallNo, CancellationToken cancellationToken)
    {
        return await _stallRepo.IsStallNoUniqueAsync(command.FacilityCode, stallNo, cancellationToken);
    }
}

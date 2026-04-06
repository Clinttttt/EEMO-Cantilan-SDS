using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;

public class UpdateStallDetailsCommandValidator : AbstractValidator<UpdateStallDetailsCommand>
{
    public UpdateStallDetailsCommandValidator()
    {
        RuleFor(x => x.ActualOccupant)
            .NotEmpty().WithMessage("Actual occupant is required")
            .MaximumLength(200).WithMessage("Actual occupant name cannot exceed 200 characters");

        RuleFor(x => x.NameOnContract)
            .MaximumLength(200).WithMessage("Name on contract cannot exceed 200 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.NameOnContract));

        RuleFor(x => x.AreaSqm)
            .GreaterThan(0).WithMessage("Area must be greater than 0")
            .When(x => x.AreaSqm.HasValue);
    }
}

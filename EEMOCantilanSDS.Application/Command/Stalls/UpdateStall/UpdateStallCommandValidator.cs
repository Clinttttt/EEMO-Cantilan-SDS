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

        // Nought is ACCEPTED here and must stay accepted: the stall DTO reports ContractYears as
        // "activeContract?.DurationYears ?? 0", so a stall with no active contract reports nought, and both edit forms pass
        // whatever they were given straight back. Requiring at least one year at this level was tried and refused a legitimate
        // edit to a stall that simply has no contract to state a term for.
        //
        // The office's ruling — that a SIGNED contract of nought years is invalid (2026-08-16) — is enforced where the
        // arrangement is actually known: the handler answers a signed nought-year edit with a stated reason, and
        // Contract.UpdateTerms refuses it outright as the last line of defence. A validator cannot tell the two cases apart,
        // because this command does not carry the arrangement.
        RuleFor(x => x.ContractYears)
            .InclusiveBetween(0, 50).WithMessage("Contract duration must be between 0 and 50 years")
            .When(x => x.ContractYears.HasValue);
    }
}

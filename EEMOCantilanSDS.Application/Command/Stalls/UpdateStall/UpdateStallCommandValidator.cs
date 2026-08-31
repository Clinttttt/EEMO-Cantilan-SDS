using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;

public class UpdateStallCommandValidator : AbstractValidator<UpdateStallCommand>
{
    private readonly IStallRepository _stallRepo;
    private readonly IFeeRateResolver _feeRateResolver;

    public UpdateStallCommandValidator(IStallRepository stallRepo, IFeeRateResolver feeRateResolver)
    {
        _stallRepo = stallRepo;
        _feeRateResolver = feeRateResolver;

        RuleFor(x => x.StallId)
            .NotEmpty().WithMessage("Stall ID is required");

        // Required only where the office HAS a monthly rent. Where its market month owes the days that month has there is
        // none, and the vendor form does not ask for one - so editing a market stall would have been refused for a figure
        // the screen never offered. Found by audit, and the reason this validator now needs to look the stall up: the
        // command carries a stall id rather than a facility, and the rule is only relaxed for a MARKET.
        RuleFor(x => x.MonthlyRate)
            .MustAsync(BeStatedWhereAMonthIsARent)
            .WithMessage("Monthly rate must be greater than ₱0");

        // Declared after the rules so the helper reads beside them rather than at the end of a long file.
        async Task<bool> BeStatedWhereAMonthIsARent(UpdateStallCommand command, decimal monthlyRate, CancellationToken ct)
        {
            if (monthlyRate > 0m) return true;

            var stall = await _stallRepo.GetByIdAsync(command.StallId, ct);
            if (stall?.Facility?.Code != FacilityCode.NPM) return false;

            var snapshot = await _feeRateResolver.GetSnapshotAsync(ct);
            return !snapshot.MonthRule.HasMonthlyGoal;
        }

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

using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.CreateStall;

public class CreateStallCommandValidator : AbstractValidator<CreateStallCommand>
{
    private readonly IStallRepository _stallRepo;
    private readonly IClock _clock;
    private readonly IFeeRateResolver _feeRateResolver;

    public CreateStallCommandValidator(IStallRepository stallRepo, IClock clock, IFeeRateResolver feeRateResolver)
    {
        _stallRepo = stallRepo;
        _clock = clock;
        _feeRateResolver = feeRateResolver;

        RuleFor(x => x.StallNo)
            .NotEmpty().WithMessage("Stall number is required")
            .MaximumLength(20).WithMessage("Stall number cannot exceed 20 characters");

        RuleFor(x => x.StallNo)
            .MustAsync(BeUniqueStallNo).WithMessage("Stall number already exists in this facility")
            .When(x => !string.IsNullOrWhiteSpace(x.StallNo));

        // A monthly figure is required only where the office HAS a monthly rent. Where its market month owes the days that
        // month has, there is no monthly amount to give: the vendor form does not ask for one, so requiring one here would
        // refuse every market stall that office tried to record. Found by audit.
        RuleFor(x => x.MonthlyRate)
            .MustAsync(BeStatedWhereAMonthIsARent)
            .WithMessage("Monthly rate must be greater than ₱0");

        RuleFor(x => x.ActualOccupant)
            .NotEmpty().WithMessage("Actual occupant is required")
            .MaximumLength(200).WithMessage("Actual occupant name cannot exceed 200 characters");

        RuleFor(x => x.ContractYears)
            .GreaterThan(0).WithMessage("Contract duration must be at least 1 year")
            .LessThanOrEqualTo(10).WithMessage("Contract duration cannot exceed 10 years")
            // Only a signed contract has a term. A space-only or extension occupancy runs until the office ends it,
            // so asking for a number of years would be asking for something the office does not have.
            .When(x => x.Arrangement == OccupancyArrangement.SignedContract);

        RuleFor(x => x.NameOnContract)
            .Empty().WithMessage("There is no signed contract, so there is no leasee name on one to record.")
            .When(x => x.Arrangement != OccupancyArrangement.SignedContract);

        // An NPM stall belongs to EITHER a canonical market section OR a per-LGU custom section — exactly
        // one. (Custom sections bill flat-daily like Vegetable/Meat.)
        RuleFor(x => x)
            .Must(x => x.Section.HasValue ^ !string.IsNullOrWhiteSpace(x.CustomSectionName))
            .WithMessage("Choose a market section or enter a custom section name (not both).")
            .When(x => x.FacilityCode == FacilityCode.NPM);

        RuleFor(x => x.CustomSectionName)
            .MaximumLength(60).WithMessage("Custom section name cannot exceed 60 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.CustomSectionName));

        RuleFor(x => x.DailyRate)
            .NotNull().WithMessage("Daily rate is required for NPM")
            .GreaterThan(0).WithMessage("Daily rate must be greater than ₱0")
            .When(x => x.FacilityCode == FacilityCode.NPM);
    }

    /// <summary>
    /// The number must be free in this facility/section — unless the office has confirmed it is taking over the
    /// stall that already carries it AND that stall is genuinely vacant (closed, or its contract has lapsed).
    /// A stall with a live contract is never reusable: that would be two occupants in one space.
    /// </summary>
    /// <summary>
    /// Whether a monthly figure had to be stated for this stall.
    /// </summary>
    /// <remarks>
    /// It has to be, unless the office measures its MARKET month by the days that month has. There is no monthly rent on
    /// that basis, the vendor form does not ask for one, and requiring one refused every market stall such an office tried
    /// to record. Every other facility, and a market on the monthly goal, are unchanged.
    /// </remarks>
    private async Task<bool> BeStatedWhereAMonthIsARent(
        CreateStallCommand command, decimal monthlyRate, CancellationToken ct)
    {
        if (monthlyRate > 0m) return true;
        if (command.FacilityCode != FacilityCode.NPM) return false;

        var snapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        return !snapshot.MonthRule.HasMonthlyGoal;
    }

    private async Task<bool> BeUniqueStallNo(CreateStallCommand command, string stallNo, CancellationToken cancellationToken)
    {
        if (await _stallRepo.IsStallNoUniqueAsync(command.FacilityCode, command.Section, command.CustomSectionName, stallNo, cancellationToken))
            return true;

        if (!command.ReuseVacatedStall)
            return false;

        var existing = await _stallRepo.FindStallByNumberAsync(
            command.FacilityCode, command.Section, command.CustomSectionName, stallNo, cancellationToken);

        return existing is not null && existing.IsVacant(_clock.PhilippineToday);
    }
}

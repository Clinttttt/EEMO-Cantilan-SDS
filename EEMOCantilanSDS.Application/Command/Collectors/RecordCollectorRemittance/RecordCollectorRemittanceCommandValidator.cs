using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;

/// <summary>
/// Refusals the office asked for, each stated in the words an officer would use.
///
/// <para>
/// The one that matters most is the ceiling: a remittance may never exceed the fee money the collector actually took in
/// the covered days. The office was explicit that anything else is bad design, so this is a refusal and not a warning.
/// Overlap is refused for the same reason: without it, two remittances could each answer for the same day's money and the
/// figure the office reconciles against would be a guess.
/// </para>
/// </summary>
public class RecordCollectorRemittanceCommandValidator : AbstractValidator<RecordCollectorRemittanceCommand>
{
    private readonly ICollectorRepository _collectors;
    private readonly ICollectorRemittanceRepository _remittances;
    private readonly IClock _clock;

    public RecordCollectorRemittanceCommandValidator(
        ICollectorRepository collectors,
        ICollectorRemittanceRepository remittances,
        IClock clock)
    {
        _collectors = collectors;
        _remittances = remittances;
        _clock = clock;

        RuleFor(x => x.CollectorId)
            .NotEmpty().WithMessage("Choose the collector who turned the money in.")
            .MustAsync(CollectorExists).WithMessage("That collector is not on this office's roster.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("A remittance is money handed over, so the amount is more than zero.");

        RuleFor(x => x.CoversTo)
            .GreaterThanOrEqualTo(x => x.CoversFrom)
            .WithMessage("The last day covered cannot fall before the first.");

        RuleFor(x => x.CoversTo)
            .Must(NotInTheFuture)
            .WithMessage("A remittance cannot cover days that have not happened yet.");

        RuleFor(x => x.ReceivedAt)
            .Must(NotAheadOfNow!)
            .When(x => x.ReceivedAt.HasValue)
            .WithMessage("The time the money was received cannot be in the future.");

        RuleFor(x => x.ReferenceNo)
            .MaximumLength(60).WithMessage("A reference number cannot exceed 60 characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(400).WithMessage("Notes cannot exceed 400 characters.");

        // Both of these read the database, so they run only once the period itself makes sense.
        RuleFor(x => x)
            .MustAsync(NotOverlapAnotherRemittance)
            .WithMessage(x => OverlapMessage)
            .When(x => x.CoversTo >= x.CoversFrom && NotInTheFuture(x.CoversTo));

        RuleFor(x => x)
            .MustAsync(NotExceedWhatWasCollected)
            .WithMessage(_ => CeilingMessage)
            .When(x => x.Amount > 0m && x.CoversTo >= x.CoversFrom && NotInTheFuture(x.CoversTo));
    }

    private string OverlapMessage = "Those days are already covered by another remittance.";
    private string CeilingMessage = "A remittance cannot exceed what was collected in those days.";

    private async Task<bool> CollectorExists(Guid id, CancellationToken ct)
        => id != Guid.Empty && await _collectors.GetByIdAsync(id, ct) is not null;

    private bool NotInTheFuture(DateOnly day) => day <= DateOnly.FromDateTime(_clock.PhilippineNow);

    private bool NotAheadOfNow(DateTime? received)
        => !received.HasValue || received.Value <= _clock.PhilippineNow.AddMinutes(2);

    private async Task<bool> NotOverlapAnotherRemittance(RecordCollectorRemittanceCommand c, CancellationToken ct)
    {
        var clash = await _remittances.FindOverlappingAsync(c.CollectorId, c.CoversFrom, c.CoversTo, null, ct);
        if (clash is null) return true;

        // Name the days and the amount, so the officer can find the record rather than hunt for it.
        OverlapMessage =
            $"{clash.CoversFrom:MMM d, yyyy} to {clash.CoversTo:MMM d, yyyy} is already covered by a remittance of " +
            $"₱{clash.Amount:N2}" + (string.IsNullOrWhiteSpace(clash.ReferenceNo) ? "" : $" ({clash.ReferenceNo})") + ".";
        return false;
    }

    private async Task<bool> NotExceedWhatWasCollected(RecordCollectorRemittanceCommand c, CancellationToken ct)
    {
        var collected = await _remittances.GetFeeCollectionsTotalAsync(c.CollectorId, c.CoversFrom, c.CoversTo, ct);
        if (c.Amount <= collected) return true;

        CeilingMessage =
            $"₱{c.Amount:N2} is more than the ₱{collected:N2} collected from {c.CoversFrom:MMM d, yyyy} to " +
            $"{c.CoversTo:MMM d, yyyy}. Electricity and water are banked separately and are not counted here.";
        return false;
    }
}

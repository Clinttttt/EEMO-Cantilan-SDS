using EEMOCantilanSDS.Domain.Common;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public class RecordDailyCollectionCommandValidator : AbstractValidator<RecordDailyCollectionCommand>
{
    public RecordDailyCollectionCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.CollectionDate).NotEmpty();
        RuleFor(x => x.ORNumber)
            .MaximumLength(30)
            .Matches(@"^[0-9A-Za-z\-]+$")
            .When(x => !string.IsNullOrWhiteSpace(x.ORNumber));
        RuleFor(x => x.FishKilos)
            .GreaterThanOrEqualTo(0)
            .When(x => x.FishKilos.HasValue);

        // Absent is mutually exclusive with a paid collection.
        RuleFor(x => x.IsPaid)
            .Equal(false)
            .When(x => x.IsAbsent)
            .WithMessage("A day cannot be both paid and absent.");

        // Absent can only be recorded for today or a past day (never the future).
        RuleFor(x => x.CollectionDate)
            .Must(d => d <= PhilippineTime.Today)
            .When(x => x.IsAbsent)
            .WithMessage("Absent can only be marked for today or a past day.");
    }
}

using EEMOCantilanSDS.Domain.Common;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public class RecordDailyCollectionCommandValidator : AbstractValidator<RecordDailyCollectionCommand>
{
    public RecordDailyCollectionCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.CollectionDate).NotEmpty();

        // MONEY cannot be collected for a day that has not happened. Nothing enforced this: the date was simply whatever the
        // caller passed, and the mobile app only ever passed today, so the gap was invisible. It stops being invisible the
        // moment a collector can choose the day - a mistyped year would file today's money under a date that has not
        // arrived, where no report would look for it.
        //
        // Scoped to a PAID day on purpose. A future ABSENT day is a deliberate, existing feature - an admin-approved
        // scheduled excused absence such as a planned closure, which owes nothing and is never counted as missed. An
        // earlier version of this rule refused those too, and the test written for that feature caught it.
        //
        // Stated here rather than in the mobile controller so it holds for every caller: the portal, the collector's app,
        // and an offline collection replayed later.
        RuleFor(x => x.CollectionDate)
            .Must(date => date <= PhilippineTime.Today)
            .When(x => x.IsPaid)
            .WithMessage("A payment cannot be recorded for a day that has not happened yet.");
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

        // Note: a future date MAY be marked Absent — this records an admin-approved *scheduled*
        // excused absence (e.g. a planned closure). It is ₱0 owed and never counts as unpaid/missed.
    }
}

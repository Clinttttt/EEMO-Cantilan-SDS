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
    }
}

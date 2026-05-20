using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public class RecordDailyCollectionCommandValidator : AbstractValidator<RecordDailyCollectionCommand>
{
    public RecordDailyCollectionCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.CollectionDate).NotEmpty();
        RuleFor(x => x.FishKilos)
            .GreaterThanOrEqualTo(0)
            .When(x => x.FishKilos.HasValue);
    }
}

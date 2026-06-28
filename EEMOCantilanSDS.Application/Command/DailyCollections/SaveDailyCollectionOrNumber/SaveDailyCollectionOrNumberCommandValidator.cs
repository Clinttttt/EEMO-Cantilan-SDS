using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;

public class SaveDailyCollectionOrNumberCommandValidator : AbstractValidator<SaveDailyCollectionOrNumberCommand>
{
    public SaveDailyCollectionOrNumberCommandValidator(IPaymentRepository paymentRepository)
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.CollectionDate).NotEqual(default(DateOnly)).WithMessage("Collection date is required.");
        RuleFor(x => x.ORNumber)
            .NotEmpty().WithMessage("OR number is required.")
            .Must(or => !string.IsNullOrWhiteSpace(or)).WithMessage("OR number is required.")
            .MaximumLength(50)
            // Check the TRIMMED value — the handler stores it trimmed, so uniqueness must match that
            // (otherwise a padded duplicate like " 123 " could slip past). Globally unique across every
            // facility's records (checked incl. soft-deleted).
            .MustAsync(async (orNumber, ct) => await paymentRepository.IsORNumberUniqueAsync((orNumber ?? string.Empty).Trim(), ct))
            .WithMessage("OR Number already exists");
    }
}

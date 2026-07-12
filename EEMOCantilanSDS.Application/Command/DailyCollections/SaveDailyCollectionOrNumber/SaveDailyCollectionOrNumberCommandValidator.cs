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
            // (otherwise a padded duplicate like " 123 " could slip past). One OR may cover several days
            // of the SAME stall (one receipt), so this is stall-aware: rejected only when the OR already
            // belongs to a different stall or another module.
            .MustAsync(async (cmd, orNumber, ct) =>
                await paymentRepository.IsDailyCollectionOrAvailableForStallAsync((orNumber ?? string.Empty).Trim(), cmd.StallId, ct))
            .WithMessage("OR Number already exists");
    }
}

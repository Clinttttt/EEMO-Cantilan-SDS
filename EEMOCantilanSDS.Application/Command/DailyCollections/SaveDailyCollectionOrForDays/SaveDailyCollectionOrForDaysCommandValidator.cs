using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;

public class SaveDailyCollectionOrForDaysCommandValidator : AbstractValidator<SaveDailyCollectionOrForDaysCommand>
{
    public SaveDailyCollectionOrForDaysCommandValidator(IPaymentRepository paymentRepository)
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Dates).NotEmpty().WithMessage("Select at least one day.");
        RuleFor(x => x.ORNumber)
            .NotEmpty().WithMessage("OR number is required.")
            .MaximumLength(50)
            // One OR may cover several days of the SAME stall (one receipt); rejected only when the OR
            // already belongs to a different stall or another module.
            .MustAsync(async (cmd, orNumber, ct) =>
                await paymentRepository.IsDailyCollectionOrAvailableForStallAsync((orNumber ?? string.Empty).Trim(), cmd.StallId, ct))
            .WithMessage("OR Number already exists");
    }
}

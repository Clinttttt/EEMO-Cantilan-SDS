using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.SaveSlaughterOrNumber;

public class SaveSlaughterOrNumberCommandValidator : AbstractValidator<SaveSlaughterOrNumberCommand>
{
    public SaveSlaughterOrNumberCommandValidator(ISlaughterRepository slaughterRepository)
    {
        RuleFor(x => x.OwnerName).NotEmpty();
        RuleFor(x => x.TransactionDate).NotEqual(default(DateOnly)).WithMessage("Transaction date is required.");
        RuleFor(x => x.ORNumber)
            .NotEmpty().WithMessage("OR number is required.")
            .MaximumLength(50)
            // The same OR may repeat within ONE receipt (this owner + date), but must be free across
            // every other module and any other slaughter receipt. Checked on the trimmed value.
            .MustAsync(async (cmd, orNumber, ct) =>
                await slaughterRepository.IsORNumberAvailableForReceiptAsync(
                    (orNumber ?? string.Empty).Trim(), cmd.OwnerName, cmd.TransactionDate, ct))
            .WithMessage("OR Number already exists");
    }
}

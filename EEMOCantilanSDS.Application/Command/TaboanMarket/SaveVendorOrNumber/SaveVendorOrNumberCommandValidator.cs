using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.SaveVendorOrNumber;

public class SaveVendorOrNumberCommandValidator : AbstractValidator<SaveVendorOrNumberCommand>
{
    public SaveVendorOrNumberCommandValidator(IOrNumberRegistry orNumbers)
    {
        RuleFor(x => x.AttendanceId).NotEmpty();
        RuleFor(x => x.ORNumber)
            .NotEmpty()
            .MaximumLength(50)
            .MustAsync(async (orNumber, ct) => await orNumbers.IsAvailableAsync(orNumber, ct))
            .WithMessage("OR number already exists.");
    }
}

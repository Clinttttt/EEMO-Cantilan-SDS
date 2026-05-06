using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.SaveTripOrNumber;

public class SaveTripOrNumberCommandValidator : AbstractValidator<SaveTripOrNumberCommand>
{
    public SaveTripOrNumberCommandValidator(ITrmRepository trmRepo)
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.ORNumber)
            .NotEmpty()
            .MaximumLength(50)
            .MustAsync(async (orNumber, ct) => await trmRepo.IsORNumberUniqueAsync(orNumber, ct))
            .WithMessage("OR number already exists.");
    }
}

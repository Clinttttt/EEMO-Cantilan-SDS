using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;

public class RecordTripCommandValidator : AbstractValidator<RecordTripCommand>
{
    public RecordTripCommandValidator(ITrmRepository trmRepo)
    {
        RuleFor(x => x.TransporterId).NotEmpty();
        RuleFor(x => x.DriverName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PlateNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Route).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ORNumber)
            .NotEmpty()
            .MaximumLength(50)
            .MustAsync(async (orNumber, ct) => await trmRepo.IsORNumberUniqueAsync(orNumber, ct))
            .WithMessage("OR number already exists.");
    }
}

using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;

public class RecordTripCommandValidator : AbstractValidator<RecordTripCommand>
{
    public RecordTripCommandValidator(ITrmRepository trmRepo)
    {
        // TransporterId is optional — an ad-hoc / walk-in trip has none. When supplied it must be a real id.
        RuleFor(x => x.TransporterId).NotEqual(Guid.Empty).When(x => x.TransporterId.HasValue);
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

using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;

public class AddTransporterCommandValidator : AbstractValidator<AddTransporterCommand>
{
    public AddTransporterCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Organization).MaximumLength(200);
        RuleFor(x => x.DefaultRoute).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PlateNumber).NotEmpty().MaximumLength(50);
    }
}

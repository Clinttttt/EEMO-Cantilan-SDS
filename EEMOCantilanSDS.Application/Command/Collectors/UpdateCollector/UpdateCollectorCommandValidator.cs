using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;

public class UpdateCollectorCommandValidator : AbstractValidator<UpdateCollectorCommand>
{
    public UpdateCollectorCommandValidator()
    {
        RuleFor(x => x.CollectorId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.AssignedFacilities).NotEmpty();
    }
}

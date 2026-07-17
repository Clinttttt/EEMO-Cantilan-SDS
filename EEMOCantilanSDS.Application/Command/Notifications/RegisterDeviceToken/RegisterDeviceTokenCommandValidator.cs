using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Notifications.RegisterDeviceToken;

public sealed class RegisterDeviceTokenCommandValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    public RegisterDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Platform).NotEmpty().MaximumLength(20);
    }
}

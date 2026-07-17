using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;

public class SetMunicipalityPaymentCredentialsCommandValidator : AbstractValidator<SetMunicipalityPaymentCredentialsCommand>
{
    public SetMunicipalityPaymentCredentialsCommandValidator()
    {
        RuleFor(x => x.SecretKey).MaximumLength(200);
        RuleFor(x => x.PublicKey).MaximumLength(200);
        RuleFor(x => x.WebhookSecret).MaximumLength(200);

        // When setting (non-empty secret), it must look like a PayMongo secret key.
        When(x => !string.IsNullOrWhiteSpace(x.SecretKey), () =>
        {
            RuleFor(x => x.SecretKey!)
                .Must(k => k.Trim().StartsWith("sk_", StringComparison.OrdinalIgnoreCase))
                .WithMessage("PayMongo secret key must start with 'sk_'.");
        });
    }
}

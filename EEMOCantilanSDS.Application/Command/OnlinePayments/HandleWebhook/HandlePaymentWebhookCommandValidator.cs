using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;

public class HandlePaymentWebhookCommandValidator : AbstractValidator<HandlePaymentWebhookCommand>
{
    public HandlePaymentWebhookCommandValidator()
    {
        // Only the body is structurally required; a missing/invalid signature is rejected
        // (fail-closed) inside the handler, not as a 400 validation error.
        RuleFor(x => x.Payload)
            .NotEmpty().WithMessage("Empty webhook payload.");
    }
}

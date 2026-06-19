using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;

public sealed class SyncOfflineCollectionsCommandValidator : AbstractValidator<SyncOfflineCollectionsCommand>
{
    public SyncOfflineCollectionsCommandValidator()
    {
        RuleFor(x => x.Operations)
            .NotNull().WithMessage("No operations to sync.")
            .Must(ops => ops.Count <= 200).WithMessage("Too many operations in one sync batch (max 200).");

        RuleForEach(x => x.Operations).ChildRules(op =>
        {
            op.RuleFor(o => o.ClientOperationId)
                .NotEmpty().WithMessage("Each operation needs a client operation id (idempotency key).");
        });
    }
}

using EEMOCantilanSDS.Domain.Constants;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;

public sealed class UpdateTenantOperationSettingCommandValidator : AbstractValidator<UpdateTenantOperationSettingCommand>
{
    public UpdateTenantOperationSettingCommandValidator()
    {
        RuleFor(x => x.OperationCode)
            .Must(TenantOperationCatalog.IsKnown).WithMessage("That is not a revenue operation StallTrack recognises.");
        // The rules that depend on the stored rows (another operation's name) are applied by the handler.
        RuleFor(x => x.LocalDisplayName)
            .MaximumLength(TenantOperationCatalog.MaxLabelLength * 2)
            .WithMessage($"A name can be at most {TenantOperationCatalog.MaxLabelLength} characters.");
    }
}

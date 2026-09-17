using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.PreflightMunicipalityActivation;

public sealed class PreflightMunicipalityActivationCommandValidator : AbstractValidator<PreflightMunicipalityActivationCommand>
{
    public PreflightMunicipalityActivationCommandValidator() =>
        RuleFor(x => x.Activation).NotNull().SetValidator(new ActivateMunicipalityCommandValidator());
}

using System.Linq;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality
{
    public class ActivateMunicipalityCommandValidator : AbstractValidator<ActivateMunicipalityCommand>
    {
        public ActivateMunicipalityCommandValidator()
        {
            RuleFor(x => x.MunicipalityCode)
                .NotEmpty().WithMessage("Municipality code is required.");

            RuleFor(x => x.Branding).NotNull();
            RuleFor(x => x.Branding.OfficeName)
                .NotEmpty().WithMessage("Office name is required.")
                .When(x => x.Branding is not null);

            RuleFor(x => x.Administrator).NotNull();
            When(x => x.Administrator is not null, () =>
            {
                RuleFor(x => x.Administrator.FullName).NotEmpty().WithMessage("Administrator full name is required.");
                RuleFor(x => x.Administrator.Username).NotEmpty().WithMessage("Administrator username is required.");
                RuleFor(x => x.Administrator.Email)
                    .NotEmpty().WithMessage("Administrator email is required.")
                    .EmailAddress().WithMessage("Administrator email is not valid.");
            });

            RuleFor(x => x.Facilities)
                .NotNull()
                .Must(f => f is not null && f.Count > 0).WithMessage("At least one facility is required to activate an LGU.");

            RuleForEach(x => x.Facilities).ChildRules(f =>
            {
                f.RuleFor(x => x.Name).NotEmpty().WithMessage("Facility name is required.");
                f.RuleFor(x => x.ShortName).NotEmpty().WithMessage("Facility short name is required.");
            });

            RuleFor(x => x.Rates).NotNull();
            RuleForEach(x => x.Rates).ChildRules(r =>
            {
                r.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0m).WithMessage("Rate amount cannot be negative.");
            });
        }
    }
}

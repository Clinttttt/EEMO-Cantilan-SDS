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
                f.RuleForEach(x => x.StallGroups).ChildRules(g =>
                {
                    g.RuleFor(s => s.Count).InclusiveBetween(1, 5000)
                        .WithMessage("Stall group count must be between 1 and 5000.");
                    g.RuleFor(s => s.MonthlyRate).GreaterThanOrEqualTo(0m)
                        .WithMessage("Stall monthly rate cannot be negative.");
                });
            });

            RuleFor(x => x.Rates).NotNull();
            RuleForEach(x => x.Rates).ChildRules(r =>
            {
                r.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0m).WithMessage("Rate amount cannot be negative.");
            });

            // Custom SLH animals are optional; when present each needs a name and a non-negative rate.
            RuleForEach(x => x.CustomAnimals).ChildRules(a =>
            {
                a.RuleFor(x => x.AnimalName).NotEmpty().WithMessage("Custom animal name is required.")
                    .MaximumLength(100).WithMessage("Custom animal name cannot exceed 100 characters.");
                a.RuleFor(x => x.RatePerHead).GreaterThanOrEqualTo(0m)
                    .WithMessage("Custom animal rate cannot be negative.");
            }).When(x => x.CustomAnimals is not null);

            // OR-series is optional; when present the format must be sane.
            When(x => x.OrSeries is not null, () =>
            {
                RuleFor(x => x.OrSeries!.Prefix).MaximumLength(30)
                    .WithMessage("OR prefix cannot exceed 30 characters.");
                RuleFor(x => x.OrSeries!.StartNumber).GreaterThanOrEqualTo(1)
                    .WithMessage("OR start number must be at least 1.");
                RuleFor(x => x.OrSeries!.PadWidth).InclusiveBetween(0, 12)
                    .WithMessage("OR pad width must be between 0 and 12.");
            });
        }
    }
}

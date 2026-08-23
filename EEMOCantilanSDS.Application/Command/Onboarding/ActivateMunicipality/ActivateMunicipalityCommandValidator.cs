using System.Linq;
using EEMOCantilanSDS.Domain.Constants;
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

            // One facility per code, per LGU. Two rows sharing a code cannot both be stored, and the database said so
            // with a bare "Conflict" that named nothing — the operator was left to guess which of an LGU's facilities
            // the platform objected to.
            RuleFor(x => x.Facilities)
                .Must(f => f is null || f.GroupBy(x => x.Code).All(g => g.Count() == 1))
                .WithMessage(x => "Two facilities were sent under the same code: " + string.Join("; ",
                    x.Facilities.GroupBy(f => f.Code).Where(g => g.Count() > 1)
                        .Select(g => $"{g.Key} ({string.Join(" and ", g.Select(f => f.Name))})"))
                    + ". A facility code identifies one facility of an LGU, so each needs its own.");

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

                // A rate key belongs to one facility's ordinance, and the resolver only reads a row filed under
                // that facility. Accepting a mis-paired row here would store a rate the billing paths then
                // ignore — the LGU would appear configured and be charged the platform's default instead.
                // Same rule the portal's own rate editor enforces (SetFacilityRateCommandValidator).
                r.RuleFor(x => x).Must(rate => FacilityRateKeys.For(rate.FacilityCode).Contains(rate.Key))
                    .WithMessage(rate =>
                        $"{rate.Key} is not a rate of {rate.FacilityCode}, so it would never be read for that facility.");
            });

            // A facility holds ONE amount per rate key, and activation files every rate on a single effective date, so
            // two rows for the same facility and key are the same statement twice. Said identically they are harmless
            // and the handler files one. Said with DIFFERENT amounts they contradict each other, and no code may pick
            // the winner — the office states its own ordinance.
            //
            // This is where Carrascal's activation failed on 2026-08-23. Its slaughterhouse listed three animals, and
            // the platform holds one large-animal rate for both carabao and cow, so two amounts arrived under
            // SlhLargePerHead. Postgres rejected the insert and the operator was shown the word "Conflict" and nothing
            // else, on an LGU that had never been activated.
            RuleFor(x => x.Rates)
                .Must(rates => rates is null || rates
                    .GroupBy(r => (r.FacilityCode, r.Key))
                    .All(g => g.Select(r => r.Amount).Distinct().Count() == 1))
                .WithMessage(x => "One rate was given two different amounts: " + string.Join("; ",
                    x.Rates.GroupBy(r => (r.FacilityCode, r.Key))
                        .Where(g => g.Select(r => r.Amount).Distinct().Count() > 1)
                        .Select(g => $"{g.Key.FacilityCode} {g.Key.Key} ({string.Join(" and ", g.Select(r => r.Amount.ToString("0.##")).Distinct())})"))
                    + ". The platform files one amount per rate, so the office states which one its ordinance charges.");

            // The same rule for the LGU's own animal registry, which is keyed by name.
            RuleFor(x => x.CustomAnimals)
                .Must(animals => animals is null || animals
                    .GroupBy(a => (a.AnimalName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .All(g => g.Select(a => a.RatePerHead).Distinct().Count() == 1))
                .WithMessage(x => "One animal was given two different rates: " + string.Join("; ",
                    x.CustomAnimals!.GroupBy(a => (a.AnimalName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Select(a => a.RatePerHead).Distinct().Count() > 1)
                        .Select(g => $"{g.Key} ({string.Join(" and ", g.Select(a => a.RatePerHead.ToString("0.##")).Distinct())})"))
                    + ". An animal carries one rate per head.");

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

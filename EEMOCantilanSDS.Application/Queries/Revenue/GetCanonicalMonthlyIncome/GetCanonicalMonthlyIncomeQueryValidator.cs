using EEMOCantilanSDS.Application.Dtos.Revenue;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;

public sealed class GetCanonicalMonthlyIncomeQueryValidator : AbstractValidator<GetCanonicalMonthlyIncomeQuery>
{
    public GetCanonicalMonthlyIncomeQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2200).WithMessage("A valid report year is required.");
        RuleFor(x => x.Month).InclusiveBetween(1, 12).When(x => x.Month.HasValue)
            .WithMessage("Month must be between 1 and 12.");
        RuleFor(x => x.Basis).IsInEnum().WithMessage("Choose the AsOf or LatestCorrected reporting basis.");
        RuleFor(x => x.AsOf).NotNull().When(x => x.Basis == CanonicalReportingBasis.AsOf)
            .WithMessage("An AsOf report requires an explicit knowledge cutoff.");
        RuleFor(x => x.AsOf).Null().When(x => x.Basis == CanonicalReportingBasis.LatestCorrected)
            .WithMessage("A LatestCorrected report uses the server's own cutoff; do not supply AsOf.");
    }
}

using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;

public class BulkImportStallholdersCommandValidator : AbstractValidator<BulkImportStallholdersCommand>
{
    // Bulk stallholder import is only meaningful for these stall-based facilities.
    private static readonly HashSet<FacilityCode> Supported = new()
    {
        FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE
    };

    public BulkImportStallholdersCommandValidator(Common.Interface.Persistence.IFacilityRepository? facilities = null)
    {
        RuleFor(x => x.FacilityCode)
            .Must(code => Supported.Contains(code) || Domain.Constants.FacilityCatalog.IsCustom(code))
            .WithMessage("Bulk import is only supported for NPM, TCC, NCC, BBQ, ICE and the office's own custom stall-rent facilities.");

        RuleFor(x => x.Rows)
            .NotEmpty().WithMessage("There are no rows to import.");

        RuleFor(x => x.Rows)
            .Must(rows => rows == null || rows.Count <= 1000)
            .WithMessage("A single import is limited to 1000 rows.");

        // NPM is collected per section, so the chosen section is mandatory for it.
        // Required for NPM, except for a market its LGU rents by the month (it imports as one monthly list). The monthly check needs the
        // facility, so it is an async rule only where a repository was supplied (the API); a bare validator keeps the plain synchronous rule.
        var sectionRule = RuleFor(x => x.Section)
            .NotNull().WithMessage("A section (Vegetable, Fish or Meat) is required for NPM imports.");
        if (facilities is null) sectionRule.When(x => x.FacilityCode == FacilityCode.NPM);
        else sectionRule.WhenAsync(async (x, ct) => x.FacilityCode == FacilityCode.NPM
            && !(await facilities.GetByCodeAsync(FacilityCode.NPM, ct) is { IsMonthlyRentMarket: true }));

        // When a section is supplied it must be a defined value (guards direct API calls).
        RuleFor(x => x.Section)
            .IsInEnum().WithMessage("The section is not a recognised market section.")
            .When(x => x.Section.HasValue);
    }
}

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

    public BulkImportStallholdersCommandValidator()
    {
        RuleFor(x => x.FacilityCode)
            .Must(code => Supported.Contains(code))
            .WithMessage("Bulk import is only supported for NPM, TCC, NCC, BBQ and ICE.");

        RuleFor(x => x.Rows)
            .NotEmpty().WithMessage("There are no rows to import.");

        RuleFor(x => x.Rows)
            .Must(rows => rows == null || rows.Count <= 1000)
            .WithMessage("A single import is limited to 1000 rows.");

        // NPM is collected per section, so the chosen section is mandatory for it.
        RuleFor(x => x.Section)
            .NotNull().WithMessage("A section (Vegetable, Fish or Meat) is required for NPM imports.")
            .When(x => x.FacilityCode == FacilityCode.NPM);

        // When a section is supplied it must be a defined value (guards direct API calls).
        RuleFor(x => x.Section)
            .IsInEnum().WithMessage("The section is not a recognised market section.")
            .When(x => x.Section.HasValue);
    }
}

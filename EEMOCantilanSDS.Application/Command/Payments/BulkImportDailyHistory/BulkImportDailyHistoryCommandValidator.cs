using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportDailyHistory;

public class BulkImportDailyHistoryCommandValidator : AbstractValidator<BulkImportDailyHistoryCommand>
{
    /// <summary>
    /// The canonical daily-billed facility. Checked here so the office is told before it prepares a file; the handler
    /// asks the facility's own archetype, which is what serves an LGU that maps its facilities differently.
    /// </summary>
    private static readonly HashSet<FacilityCode> Daily = new() { FacilityCode.NPM };

    public BulkImportDailyHistoryCommandValidator()
    {
        RuleFor(x => x.FacilityCode)
            .Must(code => Daily.Contains(code))
            .WithMessage("This import is for the market, which is collected per market day. A facility billed by the " +
                         "month has its own import, where one row is one month's payment.");

        RuleFor(x => x.Rows)
            .NotEmpty().WithMessage("There are no rows to import.");

        // A history covers years, so the cap is generous - but it is still a cap: the review grid renders an input per
        // cell, and an unbounded file would take the page down before a single row was saved.
        RuleFor(x => x.Rows)
            .Must(rows => rows == null || rows.Count <= 5000)
            .WithMessage("A single import is limited to 5000 rows. Split a longer history by year.");

        // The market's spaces are numbered per section - three of them are called "1" - so a row without a section
        // could be written against the wrong vendor's space.
        RuleFor(x => x.Section)
            .NotNull()
            .When(x => string.IsNullOrWhiteSpace(x.CustomSectionName))
            .WithMessage("Choose the market section this list belongs to. The market numbers its spaces per section, " +
                         "so the same number exists in more than one of them.");

        RuleFor(x => x.Section)
            .IsInEnum().WithMessage("The section is not a recognised market section.")
            .When(x => x.Section.HasValue);
    }
}

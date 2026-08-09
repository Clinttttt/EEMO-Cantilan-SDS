using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;

public class BulkImportPaymentHistoryCommandValidator : AbstractValidator<BulkImportPaymentHistoryCommand>
{
    /// <summary>
    /// Monthly-billed facilities only. The market bills per market day, so one row per month cannot express its
    /// history - importing it through this path would record a month's worth of daily fees as a single monthly
    /// payment and quietly settle days nobody collected. It is refused here rather than mis-recorded.
    /// </summary>
    private static readonly HashSet<FacilityCode> Supported = new()
    {
        FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE
    };

    public BulkImportPaymentHistoryCommandValidator()
    {
        RuleFor(x => x.FacilityCode)
            .Must(code => Supported.Contains(code))
            .WithMessage("Payment history can only be imported for the monthly-billed facilities: TCC, NCC, BBQ and ICE. " +
                         "The New Public Market is collected per market day, so its history is recorded separately.");

        RuleFor(x => x.Rows)
            .NotEmpty().WithMessage("There are no rows to import.");

        // A history covers years rather than one month, so the cap is higher than the stallholder import's - but it
        // is still a cap: the review grid renders an input per cell, and an unbounded file would take the page down.
        RuleFor(x => x.Rows)
            .Must(rows => rows == null || rows.Count <= 5000)
            .WithMessage("A single import is limited to 5000 rows. Split a longer history by year.");

        RuleFor(x => x.Section)
            .IsInEnum().WithMessage("The section is not a recognised market section.")
            .When(x => x.Section.HasValue);
    }
}

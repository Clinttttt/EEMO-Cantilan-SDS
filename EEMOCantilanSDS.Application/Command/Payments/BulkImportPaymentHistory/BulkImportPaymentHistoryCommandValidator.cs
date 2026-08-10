using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;

public class BulkImportPaymentHistoryCommandValidator : AbstractValidator<BulkImportPaymentHistoryCommand>
{
    /// <summary>
    /// Facilities this import can never serve, whatever an LGU has configured.
    ///
    /// <para>Stated as the ones that are NOT billed by the month rather than as a list of the ones that are: the
    /// old list named TCC, NCC, BBQ and ICE, which silently excluded every facility a Head adds for their own LGU
    /// even though those are monthly-rental and reuse this exact machinery. The facility's own archetype is the
    /// authority, and the handler checks it; this catches the canonical daily and per-unit facilities early, before a
    /// file is even read, so the office is told before it prepares one.</para>
    /// </summary>
    private static readonly HashSet<FacilityCode> NotMonthly = new()
    {
        FacilityCode.NPM, FacilityCode.TPM, FacilityCode.SLH, FacilityCode.TRM
    };

    public BulkImportPaymentHistoryCommandValidator()
    {
        RuleFor(x => x.FacilityCode)
            .Must(code => !NotMonthly.Contains(code))
            .WithMessage("This facility is not billed by the month, so its history cannot be imported one month at " +
                         "a time. The New Public Market is collected per market day and has its own import.");

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

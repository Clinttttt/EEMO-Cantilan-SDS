using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallLedgerSummary;

public class GetStallLedgerSummaryQueryValidator : AbstractValidator<GetStallLedgerSummaryQuery>
{
    public GetStallLedgerSummaryQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
    }
}

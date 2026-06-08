using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTrmHistory;

public class GetTrmHistoryQueryValidator : AbstractValidator<GetTrmHistoryQuery>
{
    public GetTrmHistoryQueryValidator()
    {
        RuleFor(x => x.Year)
            .GreaterThan(2000).WithMessage("Year must be greater than 2000.");
    }
}

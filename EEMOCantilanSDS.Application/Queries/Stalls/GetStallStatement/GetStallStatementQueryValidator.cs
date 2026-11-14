using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallStatement;

public class GetStallStatementQueryValidator : AbstractValidator<GetStallStatementQuery>
{
    public GetStallStatementQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
    }
}

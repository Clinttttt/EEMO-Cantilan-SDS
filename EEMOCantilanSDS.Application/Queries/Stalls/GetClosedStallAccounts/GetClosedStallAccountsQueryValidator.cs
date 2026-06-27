using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetClosedStallAccounts;

// No parameters to validate; present for consistency with the command/query convention.
public class GetClosedStallAccountsQueryValidator : AbstractValidator<GetClosedStallAccountsQuery>
{
    public GetClosedStallAccountsQueryValidator()
    {
    }
}

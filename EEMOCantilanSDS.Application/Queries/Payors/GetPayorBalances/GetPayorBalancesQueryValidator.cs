using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorBalances;

/// <summary>No client input to validate (payor resolved from the token); present per the
/// "every command/query has a validator" rule.</summary>
public class GetPayorBalancesQueryValidator : AbstractValidator<GetPayorBalancesQuery>
{
    public GetPayorBalancesQueryValidator()
    {
    }
}

using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;

public class GetStallHoldersListQueryValidator : AbstractValidator<GetStallHoldersListQuery>
{
    public GetStallHoldersListQueryValidator()
    {
        RuleFor(x => x.FacilityCode)
            .IsInEnum().WithMessage("Invalid facility code");
    }
}

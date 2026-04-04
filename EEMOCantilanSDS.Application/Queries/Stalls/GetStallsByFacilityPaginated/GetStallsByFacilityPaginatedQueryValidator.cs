using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacilityPaginated;

public class GetStallsByFacilityPaginatedQueryValidator : AbstractValidator<GetStallsByFacilityPaginatedQuery>
{
    public GetStallsByFacilityPaginatedQueryValidator()
    {
        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Page size cannot exceed 100");
    }
}

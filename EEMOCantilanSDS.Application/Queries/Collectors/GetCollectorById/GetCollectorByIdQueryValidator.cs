using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorById;

public class GetCollectorByIdQueryValidator : AbstractValidator<GetCollectorByIdQuery>
{
    public GetCollectorByIdQueryValidator()
    {
        RuleFor(x => x.CollectorId)
            .NotEmpty().WithMessage("Collector ID is required");
    }
}

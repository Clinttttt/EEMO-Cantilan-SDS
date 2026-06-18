using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorRecords;

public class GetCollectorRecordsQueryValidator : AbstractValidator<GetCollectorRecordsQuery>
{
    public GetCollectorRecordsQueryValidator()
    {
        RuleFor(x => x.FromDate).LessThanOrEqualTo(x => x.ToDate)
            .WithMessage("FromDate must be on or before ToDate.");
    }
}

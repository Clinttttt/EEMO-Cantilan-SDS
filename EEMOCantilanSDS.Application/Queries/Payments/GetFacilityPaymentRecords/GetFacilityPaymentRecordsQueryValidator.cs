using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetFacilityPaymentRecords;

public class GetFacilityPaymentRecordsQueryValidator : AbstractValidator<GetFacilityPaymentRecordsQuery>
{
    public GetFacilityPaymentRecordsQueryValidator()
    {
        RuleFor(x => x.FacilityCode).IsInEnum();
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

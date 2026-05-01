using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Vendors.GetVendorRegistry;

public sealed class GetVendorRegistryQueryValidator : AbstractValidator<GetVendorRegistryQuery>
{
    public GetVendorRegistryQueryValidator()
    {
        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(2100);

        RuleFor(x => x.Month)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(12);
    }
}

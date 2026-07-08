using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;

public class AddVendorToMarketDayCommandValidator : AbstractValidator<AddVendorToMarketDayCommand>
{
    public AddVendorToMarketDayCommandValidator()
    {
        RuleFor(x => x.VendorName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Goods)
            .NotEmpty()
            .MaximumLength(200);
    }
}

using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.UpdateVendor;

public class UpdateTpmVendorCommandValidator : AbstractValidator<UpdateTpmVendorCommand>
{
    public UpdateTpmVendorCommandValidator()
    {
        RuleFor(x => x.AttendanceId).NotEmpty();

        RuleFor(x => x.VendorName)
            .NotEmpty().WithMessage("Vendor name is required.")
            .MaximumLength(100);

        RuleFor(x => x.Goods)
            .NotEmpty().WithMessage("Goods / product is required.")
            .MaximumLength(100);

        RuleFor(x => x.ORNumber)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.ORNumber));
    }
}
